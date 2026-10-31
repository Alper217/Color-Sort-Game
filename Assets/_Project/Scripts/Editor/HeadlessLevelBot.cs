using System;
using System.Diagnostics;
using SandSort.Data;
using SandSort.Simulation;

namespace SandSort.EditorTools
{
    public struct BotSimulationReport
    {
        public int TotalRuns;
        public int Wins;
        public int Deadlocks;
        public float WinRatePercent;
        public float AverageMovesOnWin;
        public int MinMovesOnWin;
        public int MaxMovesOnWin;
        public long ElapsedMilliseconds;

        public string GetDifficultyVerdict()
        {
            if (TotalRuns == 0) return "Henüz Simüle Edilmedi";
            if (Wins == 0) return "İM.KANSIZ / HATALI DİZİLİM (%0 Win)";
            if (WinRatePercent < 15f) return "ÇOK ZOR (Hardcore - %1-%15)";
            if (WinRatePercent <= 65f) return "İDEAL STÜDYO DENGESİ (%15-%65)";
            if (WinRatePercent <= 88f) return "ORTA / AKICI (%65-%88)";
            return "KOLAY / ONBOARDING (%88-%100)";
        }
    }

    /// <summary>
    /// Headless AI Bot that plays a <see cref="LevelDataSO"/> thousands of times in memory
    /// using the Pure C# <see cref="BoardSimulator"/> with zero visual allocations.
    /// </summary>
    public static class HeadlessLevelBot
    {
        /// <summary>
        /// Runs <paramref name="runCount"/> headless games using a hybrid Greedy + Epsilon-Random policy.
        /// </summary>
        /// <param name="levelData">Level to evaluate.</param>
        /// <param name="runCount">Number of full games to simulate (e.g., 1000).</param>
        /// <param name="greedyStickiness">Probability (0..1) of picking the highest-scoring column vs random column.</param>
        public static BotSimulationReport RunSimulation(
            LevelDataSO levelData,
            int runCount = 1000,
            float greedyStickiness = 0.75f,
            int baseSeed = 1337)
        {
            if (levelData == null || runCount <= 0)
                return default;

            Stopwatch sw = Stopwatch.StartNew();
            BoardSimulator simulator = new BoardSimulator();

            int wins = 0;
            int deadlocks = 0;
            long totalWinMoves = 0;
            int minMoves = int.MaxValue;
            int maxMoves = 0;

            int maxColCount = levelData.Columns != null ? levelData.Columns.Length : 4;
            int[] validColumns = new int[maxColCount];
            int[] columnScores = new int[maxColCount];

            for (int run = 0; run < runCount; run++)
            {
                Random rng = new Random(baseSeed + run * 7919);
                simulator.Initialize(levelData);

                // First settle initial grid and drain any pre-existing state
                SettleBoardCompletely(simulator);

                int movesMade = 0;
                int safetyLoopCap = 500;
                bool gameFinished = false;

                while (!gameFinished && safetyLoopCap-- > 0)
                {
                    if (simulator.CheckWinCondition())
                    {
                        wins++;
                        totalWinMoves += movesMade;
                        if (movesMade < minMoves) minMoves = movesMade;
                        if (movesMade > maxMoves) maxMoves = movesMade;
                        gameFinished = true;
                        break;
                    }

                    if (simulator.CheckDeadlockCondition())
                    {
                        deadlocks++;
                        gameFinished = true;
                        break;
                    }

                    // Gather valid columns that have at least one bucket left AND if dock has a free slot
                    int validCount = 0;
                    if (simulator.FindFreeDockSlotIndex() >= 0)
                    {
                        ReadOnlySpan<int> exposed = simulator.ExposedFrontierIndices;
                        ReadOnlySpan<int> grid = simulator.Grid;

                        for (int c = 0; c < simulator.ColumnCount; c++)
                        {
                            if (simulator.CanDeployFromColumn(c) && simulator.TryPeekColumnFrontBucket(c, out BucketData frontBucket))
                            {
                                validColumns[validCount] = c;

                                // Score how many currently exposed frontier grains match this bucket's ColorId
                                int matchScore = 0;
                                for (int e = 0; e < exposed.Length; e++)
                                {
                                    if (grid[exposed[e]] == frontBucket.ColorId)
                                    {
                                        matchScore += 10;
                                    }
                                }

                                // Slight bonus if another dock slot is NOT already collecting this color
                                columnScores[validCount] = matchScore;
                                validCount++;
                            }
                        }
                    }

                    if (validCount == 0)
                    {
                        // No valid moves left: either won or deadlocked
                        if (simulator.CheckWinCondition())
                        {
                            wins++;
                            totalWinMoves += movesMade;
                            if (movesMade < minMoves) minMoves = movesMade;
                            if (movesMade > maxMoves) maxMoves = movesMade;
                        }
                        else
                        {
                            deadlocks++;
                        }
                        gameFinished = true;
                        break;
                    }

                    // Choose column: Greedy vs Exploration
                    int chosenColumn;
                    if (rng.NextDouble() <= greedyStickiness)
                    {
                        // Pick column with highest immediate exposed match score (with random tie-breaking)
                        int bestScore = -1;
                        int bestIdx = 0;
                        for (int i = 0; i < validCount; i++)
                        {
                            int jitteredScore = columnScores[i] + rng.Next(0, 5);
                            if (jitteredScore > bestScore)
                            {
                                bestScore = jitteredScore;
                                bestIdx = i;
                            }
                        }
                        chosenColumn = validColumns[bestIdx];
                    }
                    else
                    {
                        chosenColumn = validColumns[rng.Next(0, validCount)];
                    }

                    // Deploy bucket and resolve all chain reactions of reservations + gravity
                    if (simulator.TryDeployBucketFromColumn(chosenColumn, out _, out _))
                    {
                        movesMade++;
                        SettleBoardCompletely(simulator);
                    }
                    else
                    {
                        deadlocks++;
                        gameFinished = true;
                    }
                }
            }

            sw.Stop();

            return new BotSimulationReport
            {
                TotalRuns = runCount,
                Wins = wins,
                Deadlocks = deadlocks,
                WinRatePercent = runCount > 0 ? (wins * 100f) / runCount : 0f,
                AverageMovesOnWin = wins > 0 ? (float)totalWinMoves / wins : 0f,
                MinMovesOnWin = wins > 0 ? minMoves : 0,
                MaxMovesOnWin = wins > 0 ? maxMoves : 0,
                ElapsedMilliseconds = sw.ElapsedMilliseconds
            };
        }

        /// <summary>
        /// Alternates reservation steps, instant flight arrival, and gravity steps until the board reaches equilibrium.
        /// </summary>
        private static void SettleBoardCompletely(BoardSimulator simulator)
        {
            int maxIterations = 2048;
            while (maxIterations-- > 0)
            {
                int reserved = simulator.StepReservations();
                if (reserved > 0)
                {
                    simulator.FlushAllInFlightGrainsImmediate();
                }

                int moved = simulator.StepGravity();
                if (reserved == 0 && moved == 0)
                {
                    break;
                }
            }
        }
    }
}

