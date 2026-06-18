using UnityEngine;

namespace HexWorldStudio.Input
{
    /// <summary>
    /// Registers <see cref="InputSystemInputProvider"/> as the preferred
    /// <see cref="ICameraInput"/> implementation by setting
    /// <see cref="CameraInputInstaller.InputSystemFactory"/> at load
    /// time. Only compiles when <c>ENABLE_INPUT_SYSTEM</c> is defined
    /// (this asmdef is define-constrained), so installers in
    /// new-only or both-mode projects automatically pick the new
    /// provider, while legacy-only projects fall through to
    /// <see cref="LegacyUnityInputProvider"/> as if this class did not
    /// exist. See ADR-0018.
    /// </summary>
    internal static class InputSystemBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterAsRuntimePreferredProvider()
        {
            // Idempotent — calling twice produces the same final state.
            CameraInputInstaller.InputSystemFactory = AttachProviderTo;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterInEditor()
        {
            // EditMode tests run without entering Play mode, so the
            // BeforeSceneLoad hook never fires. Mirroring the
            // registration here keeps test-time AddComponent paths
            // consistent with runtime behavior.
            CameraInputInstaller.InputSystemFactory = AttachProviderTo;
        }
#endif

        private static ICameraInput AttachProviderTo(GameObject host)
        {
            return host.AddComponent<InputSystemInputProvider>();
        }
    }
}
