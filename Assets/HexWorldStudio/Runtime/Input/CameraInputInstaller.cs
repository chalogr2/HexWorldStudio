using System;
using UnityEngine;

namespace HexWorldStudio.Input
{
    /// <summary>
    /// Wires the right <see cref="ICameraInput"/> implementation onto
    /// this GameObject at <c>Awake</c> based on the project's
    /// <c>Active Input Handling</c> setting. Prefers a new-Input-System
    /// provider when one is registered via
    /// <see cref="InputSystemFactory"/>, falls back to
    /// <see cref="LegacyUnityInputProvider"/> when only
    /// <c>ENABLE_LEGACY_INPUT_MANAGER</c> is defined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// QuickStartScene wires this installer on the camera GameObject
    /// instead of either provider directly, so the demo runs unmodified
    /// under any of Unity's three input handling configurations
    /// (Legacy, New, Both). Consumers — <c>CivCameraController</c>,
    /// <c>TileHover</c>, <c>TileSelection</c> — keep reading through
    /// <see cref="ICameraInput"/> via <c>GetComponent&lt;ICameraInput&gt;()</c>;
    /// nothing changes on the consumer side.
    /// </para>
    /// <para>
    /// The new-Input-System provider lives in a separate define-constrained
    /// asmdef (<c>HexWorldStudio.Runtime.InputSystem</c>), so the main
    /// runtime assembly can't reference its type directly. Instead the
    /// InputSystem asmdef registers a factory delegate at load time via
    /// <c>RuntimeInitializeOnLoadMethod</c>; this installer prefers
    /// that factory when present and falls back to the legacy provider
    /// when it's not. See ADR-0018.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class CameraInputInstaller : MonoBehaviour
    {
        /// <summary>
        /// Set by the new-Input-System asmdef (if present) at load time
        /// to a delegate that <c>AddComponent</c>s its provider onto the
        /// installer's GameObject. <c>null</c> when the new Input System
        /// is not active — in that case the installer falls back to the
        /// legacy provider.
        /// </summary>
        public static Func<GameObject, ICameraInput> InputSystemFactory;

        /// <summary>
        /// The provider instance installed by this component, or
        /// <c>null</c> before <c>Awake</c> has run. Useful for tests
        /// and editor inspection.
        /// </summary>
        public ICameraInput Installed { get; private set; }

        private void Awake()
        {
            // If a provider is already present (e.g. wired manually for a buyer override), don't replace it.
            ICameraInput existing = GetComponent<ICameraInput>();
            if (existing != null)
            {
                Installed = existing;
                return;
            }

            if (InputSystemFactory != null)
            {
                Installed = InputSystemFactory(gameObject);
                if (Installed != null) return;
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            Installed = gameObject.AddComponent<LegacyUnityInputProvider>();
#else
            if (Installed == null)
            {
                Debug.LogWarning(
                    "CameraInputInstaller: no input provider could be installed. The new Input System didn't register a factory and ENABLE_LEGACY_INPUT_MANAGER isn't defined. Check Active Input Handling in Player Settings.", this);
            }
#endif
        }
    }
}
