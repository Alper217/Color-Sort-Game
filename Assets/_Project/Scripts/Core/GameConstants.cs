using UnityEngine;

namespace SandSort.Core
{
    /// <summary>
    /// Global constants and pre-hashed shader property IDs to eliminate string allocations.
    /// Configured for 60x60 = 3,600 GPU-instanced sand grains.
    /// </summary>
    public static class GameConstants
    {
        public const int EmptyCellColorId = -1;
        public const int DefaultGridWidth = 60;
        public const int DefaultGridHeight = 60;
        public const int DefaultDockCapacity = 4;
        public const int DefaultQueueColumnCount = 4;

        // Timing & Game Feel
        public const float DefaultGravityStepInterval = 0.025f;
        public const float DefaultGrainEmitInterval = 0.004f;
        public const float DefaultGrainFlightDuration = 0.32f;

        // Pre-hashed Shader IDs
        public static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
        public static readonly int MainColorShaderId = Shader.PropertyToID("_Color");
        public static readonly int FillAmountShaderId = Shader.PropertyToID("_FillAmount");

        /// <summary>
        /// High-contrast 6-color studio puzzle palette (Zero similar tones).
        /// </summary>
        public static readonly Color32[] DefaultVibrantPalette = new Color32[]
        {
            new Color32(239, 59, 77, 255),   // 0: Vibrant Coral Red
            new Color32(45, 125, 246, 255),  // 1: Royal Azure Blue
            new Color32(255, 212, 38, 255),  // 2: Pure Golden Yellow
            new Color32(38, 203, 108, 255),  // 3: Emerald Green
            new Color32(165, 84, 245, 255),  // 4: Amethyst Purple
            new Color32(34, 211, 238, 255)   // 5: Bright Sky Cyan
        };
    }
}
