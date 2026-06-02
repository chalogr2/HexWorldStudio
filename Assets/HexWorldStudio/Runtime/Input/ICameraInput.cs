using UnityEngine;

namespace HexWorldStudio.Input
{
    /// <summary>
    /// Abstraction over the input the demo camera controller reads.
    /// Buyers swap implementations to migrate between the legacy
    /// <c>UnityEngine.Input</c> class and the newer Input System
    /// package without touching the camera controller code.
    /// </summary>
    /// <remarks>
    /// All axes are normalized to roughly <c>[-1, 1]</c> where it makes
    /// sense (Pan, Rotate, Tilt). <see cref="ZoomDelta"/> mirrors
    /// scroll-wheel deltas — sign positive means "zoom in" (camera
    /// moves closer / lower).
    /// </remarks>
    public interface ICameraInput
    {
        /// <summary>
        /// Two-axis pan: X is right-positive, Y is forward-positive (i.e.,
        /// "up screen" in a top-down camera). Combines keyboard input
        /// (WASD / arrows) with edge-scroll if enabled by the
        /// implementation.
        /// </summary>
        Vector2 PanAxis { get; }

        /// <summary>
        /// Scroll-wheel zoom delta. Positive means "zoom in" (camera
        /// moves closer / lower). One scroll click is approximately
        /// <c>±0.1</c> in Unity's legacy Input.
        /// </summary>
        float ZoomDelta { get; }

        /// <summary>
        /// Yaw rotate axis. Positive means "rotate right" (clockwise
        /// when viewed from above). Combines keyboard keys
        /// (Q/E or similar) with mouse-drag deltas when the
        /// implementation supports drag input.
        /// </summary>
        float RotateAxis { get; }

        /// <summary>
        /// Pitch (tilt) axis. Positive means "tilt camera up" (look more
        /// down at the world). Combines keyboard keys with right-mouse
        /// drag when the implementation supports drag input.
        /// </summary>
        float TiltAxis { get; }

        /// <summary>
        /// True when the mouse cursor is inside the game window.
        /// Implementations that don't know about window focus may
        /// return <c>true</c> unconditionally.
        /// </summary>
        bool IsMouseInWindow { get; }

        /// <summary>Mouse cursor position in pixels, origin at the bottom-left.</summary>
        Vector2 MousePosition { get; }
    }
}
