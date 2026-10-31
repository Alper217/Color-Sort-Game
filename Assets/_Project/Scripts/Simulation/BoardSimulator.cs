using System;
using SandSort.Core;
using SandSort.Data;

namespace SandSort.Simulation
{
    /// <summary>
    /// Pure C# deterministic Sandbox puzzle simulation engine (Zero-GC in steady state).
    /// Supports dynamic resolution resampling (e.g., 60x60 = 3,600 up to 120x120 = 14,400 grains)
    /// while automatically scaling bucket capacities to maintain 100% conservation of mass!
    /// </summary>
    public sealed class BoardSimulator
    {
        public const int VacatedInteriorCell = -1;

        private int _width;
        private int _height;
        private GravityMode _gravityMode;
        private int _dockCapacity;
        private int _columnCount;

        // Pre-allocated simulation buffers
        private int[] _grid = Array.Empty<int>();
        private int[] _columnHeights = Array.Empty<int>();
        private int[] _columnCompactionCounts = Array.Empty<int>();
        private int[] _exposedFrontierIndices = Array.Empty<int>();
        private int _exposedCount;

        // Spawner columns
        private BucketData[][] _columns = Array.Empty<BucketData[]>();
        private int[] _columnHeads = Array.Empty<int>();
        private int[] _columnLengths = Array.Empty<int>();

        // Active Dock slots
        private ActiveDockSlotState[] _dockSlots = Array.Empty<ActiveDockSlotState>();

        // Output buffers for visual synchronization (Zero-GC)
        private GrainMoveRecord[] _moveBuffer = Array.Empty<GrainMoveRecord>();
        private int _moveCount;

        private GrainReservationRecord[] _reservationBuffer = Array.Empty<GrainReservationRecord>();
        private int _reservationCount;

        private int _remainingGridGrains;
        private int _totalInFlightGrains;
        private int _stepTick;
        private int _scanColumnCursor;
        private int[] _slotReservationsThisPass = new int[16];

        public int Width => _width;
        public int Height => _height;
        public GravityMode Gravity => _gravityMode;
        public int DockCapacity => _dockCapacity;
        public int ColumnCount => _columnCount;
        public int RemainingGridGrains => _remainingGridGrains;
        public int TotalInFlightGrains => _totalInFlightGrains;
        public int ExposedCount => _exposedCount;

        public ReadOnlySpan<int> Grid => new ReadOnlySpan<int>(_grid, 0, _width * _height);
        public ReadOnlySpan<int> ColumnHeights => new ReadOnlySpan<int>(_columnHeights, 0, _width);
        public ReadOnlySpan<int> LastStepColumnCompactions => new ReadOnlySpan<int>(_columnCompactionCounts, 0, _width);
        public ReadOnlySpan<int> ExposedFrontierIndices => new ReadOnlySpan<int>(_exposedFrontierIndices, 0, _exposedCount);
        public ReadOnlySpan<ActiveDockSlotState> DockSlots => new ReadOnlySpan<ActiveDockSlotState>(_dockSlots, 0, _dockCapacity);
        public ReadOnlySpan<GrainMoveRecord> LastStepMoves => new ReadOnlySpan<GrainMoveRecord>(_moveBuffer, 0, _moveCount);
        public ReadOnlySpan<GrainReservationRecord> LastStepReservations => new ReadOnlySpan<GrainReservationRecord>(_reservationBuffer, 0, _reservationCount);

        /// <summary>
        /// Initializes the simulation from <paramref name="levelData"/>.
        /// If <paramref name="overrideResolution"/> &gt; 0, resamples the sand matrix to
        /// (overrideResolution x overrideResolution) and proportionally adjusts bucket capacities
        /// so total bucket capacity equals total sand grains 100%!
        /// </summary>
        public void Initialize(LevelDataSO levelData, int overrideResolution = 0)
        {
            if (levelData == null) throw new ArgumentNullException(nameof(levelData));

            int targetW = overrideResolution > 8 ? overrideResolution : levelData.Width;
            int targetH = overrideResolution > 8 ? overrideResolution : levelData.Height;
            int colCount = levelData.Columns != null ? levelData.Columns.Length : 0;

            EnsureCapacity(targetW, targetH, levelData.ActiveDockCapacity, colCount);

            _width = targetW;
            _height = targetH;
            _gravityMode = levelData.Gravity;
            _dockCapacity = levelData.ActiveDockCapacity;
            _columnCount = colCount;
            _stepTick = 0;
            _moveCount = 0;
            _reservationCount = 0;
            _totalInFlightGrains = 0;
            _remainingGridGrains = 0;

            int srcW = Math.Max(1, levelData.Width);
            int srcH = Math.Max(1, levelData.Height);
            int paletteLen = (levelData.Palette != null && levelData.Palette.Length > 0) ? levelData.Palette.Length : 16;
            Span<int> actualColorTotals = stackalloc int[paletteLen];

            // Nearest-neighbor resample from levelData.GridCells into (_width x _height)
            for (int y = 0; y < _height; y++)
            {
                int srcY = (y * srcH) / _height;
                for (int x = 0; x < _width; x++)
                {
                    int srcX = (x * srcW) / _width;
                    int srcIdx = srcY * srcW + srcX;
                    int dstIdx = y * _width + x;

                    int colorId = (levelData.GridCells != null && srcIdx < levelData.GridCells.Length)
                        ? levelData.GridCells[srcIdx]
                        : GameConstants.EmptyCellColorId;

                    if (colorId >= 0)
                    {
                        _grid[dstIdx] = colorId;
                        _remainingGridGrains++;
                        if (colorId < paletteLen)
                        {
                            actualColorTotals[colorId]++;
                        }
                    }
                    else
                    {
                        _grid[dstIdx] = VacatedInteriorCell;
                    }
                }
            }

            CompactAllColumnsImmediate();

            // Copy spawner columns and rebalance capacities to match resampled grain counts 100%
            Span<int> bucketColorTotals = stackalloc int[paletteLen];
            Span<int> lastBucketColForColor = stackalloc int[paletteLen];
            Span<int> lastBucketIdxForColor = stackalloc int[paletteLen];
            for (int i = 0; i < paletteLen; i++)
            {
                lastBucketColForColor[i] = -1;
                lastBucketIdxForColor[i] = -1;
            }

            float scaleRatio = (float)(_width * _height) / Math.Max(1, srcW * srcH);

            for (int c = 0; c < _columnCount; c++)
            {
                BucketData[] src = levelData.Columns[c].Buckets;
                int len = src != null ? src.Length : 0;
                if (_columns[c] == null || _columns[c].Length < len)
                {
                    _columns[c] = new BucketData[Math.Max(len, 16)];
                }

                for (int b = 0; b < len; b++)
                {
                    BucketData bd = src[b];
                    int scaledCap = Math.Max(1, (int)Math.Round(bd.MaxCapacity * scaleRatio));
                    _columns[c][b] = new BucketData(bd.ColorId, scaledCap);

                    if (bd.ColorId >= 0 && bd.ColorId < paletteLen)
                    {
                        bucketColorTotals[bd.ColorId] += scaledCap;
                        lastBucketColForColor[bd.ColorId] = c;
                        lastBucketIdxForColor[bd.ColorId] = b;
                    }
                }

                _columnHeads[c] = 0;
                _columnLengths[c] = len;
            }

            // Exact remainder correction per color so sum(Buckets) == actualColorTotals[color] 100%
            for (int colorId = 0; colorId < paletteLen; colorId++)
            {
                int diff = actualColorTotals[colorId] - bucketColorTotals[colorId];
                int lastC = lastBucketColForColor[colorId];
                int lastB = lastBucketIdxForColor[colorId];

                if (diff != 0 && lastC >= 0 && lastB >= 0)
                {
                    BucketData prev = _columns[lastC][lastB];
                    int newCap = Math.Max(1, prev.MaxCapacity + diff);
                    _columns[lastC][lastB] = new BucketData(colorId, newCap);
                }
            }

            // Enforce 50-100 grain capacity per bucket (and progressively more buckets on higher levels!)
            int targetMaxPerBucket = levelData.LevelIndex <= 1 ? 100 : (levelData.LevelIndex == 2 ? 80 : 65);
            int targetChunkSize = levelData.LevelIndex <= 1 ? 85 : (levelData.LevelIndex == 2 ? 70 : 55);
            SubdivideOversizedBuckets(targetMaxPerBucket, targetChunkSize);

            // Level Progression Column Count:
            // Levels 1..10 -> 2 Columns
            // Levels 11..20 -> 3 Columns
            // Levels 21+ -> 4 Columns
            int targetColumnCount = levelData.LevelIndex <= 10 ? 2 : (levelData.LevelIndex <= 20 ? 3 : 4);
            if (_columnCount != targetColumnCount && _columnCount > 0)
            {
                RedistributeIntoTargetColumns(targetColumnCount);
            }

            for (int s = 0; s < _dockCapacity; s++)
            {
                _dockSlots[s].Clear();
            }

            ComputeExposedFrontier();
        }

        private void RedistributeIntoTargetColumns(int targetColCount)
        {
            targetColCount = Math.Clamp(targetColCount, 1, 6);
            int totalBuckets = 0;
            int maxLen = 0;
            for (int c = 0; c < _columnCount; c++)
            {
                totalBuckets += _columnLengths[c];
                if (_columnLengths[c] > maxLen) maxLen = _columnLengths[c];
            }

            if (totalBuckets == 0) return;

            BucketData[][] newCols = new BucketData[targetColCount][];
            int[] newLens = new int[targetColCount];
            for (int tc = 0; tc < targetColCount; tc++)
            {
                newCols[tc] = new BucketData[(totalBuckets / targetColCount) + 4];
            }

            // Interleave row-by-row across the source columns so front-of-queue solvability order is preserved!
            int assignCounter = 0;
            for (int row = 0; row < maxLen; row++)
            {
                for (int srcCol = 0; srcCol < _columnCount; srcCol++)
                {
                    if (row < _columnLengths[srcCol])
                    {
                        int dstCol = assignCounter % targetColCount;
                        int writeIdx = newLens[dstCol];
                        if (writeIdx >= newCols[dstCol].Length)
                        {
                            Array.Resize(ref newCols[dstCol], newCols[dstCol].Length * 2);
                        }
                        newCols[dstCol][writeIdx] = _columns[srcCol][row];
                        newLens[dstCol] = writeIdx + 1;
                        assignCounter++;
                    }
                }
            }

            _columnCount = targetColCount;
            EnsureCapacity(_width, _height, _dockCapacity, _columnCount);
            for (int tc = 0; tc < targetColCount; tc++)
            {
                _columns[tc] = newCols[tc];
                _columnHeads[tc] = 0;
                _columnLengths[tc] = newLens[tc];
            }
        }

        private void SubdivideOversizedBuckets(int maxAllowedPerBucket, int preferredChunkSize)
        {
            for (int c = 0; c < _columnCount; c++)
            {
                int currentLen = _columnLengths[c];
                if (currentLen <= 0) continue;

                // Count how many buckets we will have after splitting any bucket > maxAllowedPerBucket
                int newCount = 0;
                for (int b = 0; b < currentLen; b++)
                {
                    int cap = _columns[c][b].MaxCapacity;
                    if (cap > maxAllowedPerBucket)
                    {
                        int parts = Math.Max(2, (int)Math.Ceiling((double)cap / preferredChunkSize));
                        newCount += parts;
                    }
                    else
                    {
                        newCount++;
                    }
                }

                if (newCount == currentLen) continue;

                BucketData[] expanded = new BucketData[newCount];
                int writeIdx = 0;
                for (int b = 0; b < currentLen; b++)
                {
                    BucketData bd = _columns[c][b];
                    if (bd.MaxCapacity > maxAllowedPerBucket)
                    {
                        int parts = Math.Max(2, (int)Math.Ceiling((double)bd.MaxCapacity / preferredChunkSize));
                        int baseCap = bd.MaxCapacity / parts;
                        int rem = bd.MaxCapacity % parts;
                        for (int p = 0; p < parts; p++)
                        {
                            int partCap = baseCap + (p < rem ? 1 : 0);
                            expanded[writeIdx++] = new BucketData(bd.ColorId, partCap);
                        }
                    }
                    else
                    {
                        expanded[writeIdx++] = bd;
                    }
                }

                _columns[c] = expanded;
                _columnLengths[c] = writeIdx;
            }
        }

        private void EnsureCapacity(int width, int height, int dockCap, int colCount)
        {
            int cellCount = width * height;
            if (_grid.Length < cellCount)
            {
                _grid = new int[cellCount];
                _moveBuffer = new GrainMoveRecord[cellCount];
                _reservationBuffer = new GrainReservationRecord[cellCount];
            }

            if (_exposedFrontierIndices.Length < width)
            {
                _exposedFrontierIndices = new int[width];
                _columnHeights = new int[width];
                _columnCompactionCounts = new int[width];
            }

            if (_dockSlots.Length < dockCap)
            {
                _dockSlots = new ActiveDockSlotState[dockCap];
            }

            if (_columns.Length < colCount)
            {
                _columns = new BucketData[colCount][];
                _columnHeads = new int[colCount];
                _columnLengths = new int[colCount];
            }
        }

        private void CompactAllColumnsImmediate()
        {
            for (int x = 0; x < _width; x++)
            {
                int writeY = 0;
                for (int readY = 0; readY < _height; readY++)
                {
                    int colorId = _grid[readY * _width + x];
                    if (colorId >= 0)
                    {
                        if (writeY != readY)
                        {
                            _grid[writeY * _width + x] = colorId;
                            _grid[readY * _width + x] = VacatedInteriorCell;
                        }
                        writeY++;
                    }
                }
                _columnHeights[x] = writeY;
                _columnCompactionCounts[x] = 0;
            }
        }

        public int GetCell(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height)
                return GameConstants.EmptyCellColorId;
            return _grid[y * _width + x];
        }

        public int GetColumnBucketCount(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= _columnCount) return 0;
            return _columnLengths[columnIndex];
        }

        public BucketData GetColumnBucketAt(int columnIndex, int indexInColumn)
        {
            return _columns[columnIndex][indexInColumn];
        }

        public bool CanDeployFromColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= _columnCount) return false;
            if (_columnHeads[columnIndex] >= _columnLengths[columnIndex]) return false;
            return FindFreeDockSlotIndex() >= 0;
        }

        public int GetRemainingBucketsInColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= _columnCount) return 0;
            int remaining = _columnLengths[columnIndex] - _columnHeads[columnIndex];
            return remaining > 0 ? remaining : 0;
        }

        public bool TryPeekColumnFrontBucket(int columnIndex, out BucketData bucket)
        {
            if (columnIndex >= 0 && columnIndex < _columnCount && _columnHeads[columnIndex] < _columnLengths[columnIndex])
            {
                bucket = _columns[columnIndex][_columnHeads[columnIndex]];
                return true;
            }

            bucket = default;
            return false;
        }

        public int FindFreeDockSlotIndex()
        {
            for (int i = 0; i < _dockCapacity; i++)
            {
                if (!_dockSlots[i].IsOccupied)
                    return i;
            }
            return -1;
        }

        public bool TryDeployBucketFromColumn(int columnIndex, out int assignedDockSlotIndex, out BucketData deployedBucket)
        {
            assignedDockSlotIndex = -1;
            deployedBucket = default;

            if (columnIndex < 0 || columnIndex >= _columnCount) return false;
            int head = _columnHeads[columnIndex];
            if (head >= _columnLengths[columnIndex]) return false;

            int freeSlot = FindFreeDockSlotIndex();
            if (freeSlot < 0) return false;

            deployedBucket = _columns[columnIndex][head];
            _columnHeads[columnIndex] = head + 1;

            _dockSlots[freeSlot].Assign(deployedBucket);
            assignedDockSlotIndex = freeSlot;
            return true;
        }

        public void SetSlotConveyorState(int slotIndex, bool isInCollectionZone, float normalizedBottomX)
        {
            if (slotIndex < 0 || slotIndex >= _dockCapacity) return;
            _dockSlots[slotIndex].IsInCollectionZone = isInCollectionZone;
            _dockSlots[slotIndex].NormalizedBottomX = Math.Clamp(normalizedBottomX, 0f, 1f);
        }

        public void ComputeExposedFrontier()
        {
            _exposedCount = 0;
            for (int x = 0; x < _width; x++)
            {
                if (_columnHeights[x] > 0 && _grid[x] >= 0)
                {
                    _exposedFrontierIndices[_exposedCount++] = x;
                }
            }
        }

        public int StepReservations(int maxReservationsPerSlot = int.MaxValue)
        {
            _reservationCount = 0;
            Array.Clear(_columnCompactionCounts, 0, _width);
            ComputeExposedFrontier();

            if (_exposedCount == 0 || _width <= 0) return 0;

            if (_slotReservationsThisPass.Length < _dockCapacity)
            {
                _slotReservationsThisPass = new int[_dockCapacity];
            }
            Array.Clear(_slotReservationsThisPass, 0, _dockCapacity);

            // For each active bucket currently on the Bottom Conveyor Segment (IsInCollectionZone == true),
            // scan outward from the bucket's current horizontal X position along the bottom row (y = 0)
            for (int s = 0; s < _dockCapacity; s++)
            {
                if (!_dockSlots[s].CanRequestMore || !_dockSlots[s].IsInCollectionZone)
                    continue;

                int targetColor = _dockSlots[s].ColorId;
                int centerCol = Math.Clamp((int)Math.Round(_dockSlots[s].NormalizedBottomX * (_width - 1)), 0, _width - 1);

                for (int offset = 0; offset < _width; offset++)
                {
                    if (!_dockSlots[s].CanRequestMore || _slotReservationsThisPass[s] >= maxReservationsPerSlot)
                        break;

                    // Check centerCol + offset and centerCol - offset
                    for (int dir = 0; dir < 2; dir++)
                    {
                        if (offset == 0 && dir == 1) break;
                        int x = (dir == 0) ? (centerCol + offset) : (centerCol - offset);
                        if (x < 0 || x >= _width) continue;

                        if (_columnHeights[x] <= 0 || _grid[x] != targetColor)
                            continue;

                        _slotReservationsThisPass[s]++;
                        _remainingGridGrains--;
                        _dockSlots[s].InFlightCount++;
                        _totalInFlightGrains++;

                        _reservationBuffer[_reservationCount++] = new GrainReservationRecord(
                            x, x, 0, targetColor, s);

                        int colH = _columnHeights[x];
                        for (int y = 0; y < colH - 1; y++)
                        {
                            _grid[y * _width + x] = _grid[(y + 1) * _width + x];
                        }
                        _grid[(colH - 1) * _width + x] = VacatedInteriorCell;
                        _columnHeights[x] = colH - 1;
                        _columnCompactionCounts[x]++;

                        if (!_dockSlots[s].CanRequestMore || _slotReservationsThisPass[s] >= maxReservationsPerSlot)
                            break;
                    }
                }
            }

            if (_reservationCount > 0)
            {
                ComputeExposedFrontier();
            }

            return _reservationCount;
        }

        public bool ConfirmGrainArrival(int dockSlotIndex, out ActiveDockSlotState updatedState, out bool bucketCompleted)
        {
            bucketCompleted = false;
            if (dockSlotIndex < 0 || dockSlotIndex >= _dockCapacity || !_dockSlots[dockSlotIndex].IsOccupied)
            {
                updatedState = default;
                return false;
            }

            if (_dockSlots[dockSlotIndex].InFlightCount > 0)
            {
                _dockSlots[dockSlotIndex].InFlightCount--;
                _totalInFlightGrains = Math.Max(0, _totalInFlightGrains - 1);
            }

            _dockSlots[dockSlotIndex].CurrentCount++;
            updatedState = _dockSlots[dockSlotIndex];

            if (_dockSlots[dockSlotIndex].IsFull)
            {
                bucketCompleted = true;
                _dockSlots[dockSlotIndex].Clear();
            }

            return true;
        }

        public void FlushAllInFlightGrainsImmediate()
        {
            for (int s = 0; s < _dockCapacity; s++)
            {
                if (_dockSlots[s].IsOccupied && _dockSlots[s].InFlightCount > 0)
                {
                    _dockSlots[s].CurrentCount += _dockSlots[s].InFlightCount;
                    _dockSlots[s].InFlightCount = 0;

                    if (_dockSlots[s].IsFull)
                    {
                        _dockSlots[s].Clear();
                    }
                }
            }
            _totalInFlightGrains = 0;
        }

        public int StepGravity()
        {
            _moveCount = 0;
            _stepTick++;

            if (_gravityMode != GravityMode.CellularAutomata || _remainingGridGrains <= 0)
            {
                return 0;
            }

            for (int pass = 0; pass < 3; pass++)
            {
                bool leftToRight = (((_stepTick + pass) & 1) == 0);

                for (int i = 0; i < _width; i++)
                {
                    int x = leftToRight ? i : (_width - 1 - i);
                    int h = _columnHeights[x];
                    if (h <= 0) continue;

                    int firstDir = (((x + _stepTick + pass) & 1) == 0) ? -1 : 1;
                    int secondDir = -firstDir;

                    if (TryTumbleTopGrain(x, x + firstDir))
                        continue;

                    TryTumbleTopGrain(x, x + secondDir);
                }
            }

            if (_moveCount > 0)
            {
                ComputeExposedFrontier();
            }

            return _moveCount;
        }

        private bool TryTumbleTopGrain(int fromX, int toX)
        {
            if (toX < 0 || toX >= _width) return false;

            int fromH = _columnHeights[fromX];
            int toH = _columnHeights[toX];

            bool shouldTumble = (fromH - toH >= 2) || (toH == 0 && fromH >= 1);
            if (!shouldTumble) return false;

            int srcY = fromH - 1;
            int dstY = toH;

            int srcIdx = srcY * _width + fromX;
            int dstIdx = dstY * _width + toX;

            int colorId = _grid[srcIdx];
            if (colorId < 0) return false;

            _grid[dstIdx] = colorId;
            _grid[srcIdx] = VacatedInteriorCell;

            _columnHeights[fromX] = fromH - 1;
            _columnHeights[toX] = toH + 1;

            if (_moveCount < _moveBuffer.Length)
            {
                _moveBuffer[_moveCount++] = new GrainMoveRecord(srcIdx, dstIdx, fromX, srcY, toX, dstY, colorId);
            }

            return true;
        }

        public bool HasPendingGravityMoves()
        {
            if (_gravityMode != GravityMode.CellularAutomata || _remainingGridGrains <= 0)
                return false;

            for (int x = 0; x < _width; x++)
            {
                int h = _columnHeights[x];
                if (h <= 0) continue;

                if (x > 0)
                {
                    int leftH = _columnHeights[x - 1];
                    if (h - leftH >= 2 || (leftH == 0 && h >= 1)) return true;
                }

                if (x + 1 < _width)
                {
                    int rightH = _columnHeights[x + 1];
                    if (h - rightH >= 2 || (rightH == 0 && h >= 1)) return true;
                }
            }

            return false;
        }

        public bool CheckWinCondition()
        {
            return _remainingGridGrains == 0 && _totalInFlightGrains == 0;
        }

        public bool CheckDeadlockCondition()
        {
            if (_remainingGridGrains == 0) return false;
            if (_totalInFlightGrains > 0) return false;

            for (int s = 0; s < _dockCapacity; s++)
            {
                if (!_dockSlots[s].IsOccupied)
                    return false;
            }

            if (HasPendingGravityMoves())
                return false;

            ComputeExposedFrontier();
            for (int e = 0; e < _exposedCount; e++)
            {
                int colorId = _grid[_exposedFrontierIndices[e]];
                if (colorId < 0) continue;

                for (int s = 0; s < _dockCapacity; s++)
                {
                    if (_dockSlots[s].CanRequestMore && _dockSlots[s].ColorId == colorId)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
