using System;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Input;
using HexWorldStudio.Rendering;
using UnityEngine;

namespace HexWorldStudio.Examples.QuickStart
{
    /// <summary>
    /// Selection layer for the demo scene. On <see cref="ICameraInput.PrimaryClickPressed"/>
    /// it commits the current <see cref="TileHover"/>'s coord as the
    /// selected tile and parks a <see cref="HexOutline"/> at that
    /// tile's world position. On <see cref="ICameraInput.SecondaryClickPressed"/>
    /// it clears the selection. The selection also clears when the
    /// world regenerates (the new world has different tiles, so a
    /// stale coord may point at unrelated data).
    /// </summary>
    /// <remarks>
    /// Like <see cref="TileHover"/>, the production loop is in
    /// <see cref="Tick"/> rather than <see cref="Update"/> so PlayMode
    /// tests and buyer scripts can advance the state machine
    /// deterministically.
    /// </remarks>
    public sealed class TileSelection : MonoBehaviour
    {
        [Header("Scene wiring")]
        [SerializeField] private TileHover _hover;
        [SerializeField] private WorldGeneratorBootstrap _bootstrap;

        [Tooltip("MonoBehaviour implementing ICameraInput. If null, searches the GameObject for one.")]
        [SerializeField] private MonoBehaviour _inputSource;

        [Header("Outline visual")]
        [SerializeField] private Color _outlineColor = new Color(1f, 0.78f, 0.30f, 1f);
        [Range(0.01f, 0.5f)]
        [SerializeField] private float _outlineWidth = 0.08f;

        private ICameraInput _input;
        private GameObject _outline;
        private HexCoord? _selectedCoord;
        private bool _subscribedToBootstrap;

        /// <summary>Currently selected tile, or <c>null</c> when nothing is selected.</summary>
        public HexCoord? SelectedCoord => _selectedCoord;

        /// <summary>Fires whenever the selection changes (including transitions to / from null).</summary>
        public event Action<HexCoord?> OnSelectionChanged;

        private void Awake()
        {
            _outline = HexOutline.Create("SelectionOutline", _outlineColor, _outlineWidth);
            _outline.transform.SetParent(transform, worldPositionStays: false);
            SetOutlineVisible(false);
        }

        private void OnDestroy()
        {
            if (_outline != null) UnityEngine.Object.Destroy(_outline);
            if (_bootstrap != null) _bootstrap.OnRegenerated -= ClearSelectionOnRegenerate;
        }

        private void Update()
        {
            Tick();
        }

        /// <summary>
        /// One step of the selection loop. Mirrors
        /// <see cref="TileHover.Tick"/> — call this explicitly from
        /// tests or buyer scripts that want deterministic single-step
        /// control instead of relying on <see cref="Update"/>.
        /// </summary>
        public void Tick()
        {
            if (_input == null) ResolveInput();
            if (_input == null) return;
            EnsureBootstrapSubscription();

            if (_input.SecondaryClickPressed)
            {
                ClearSelection();
                return;
            }

            if (_input.PrimaryClickPressed && _hover != null && _hover.HoveredCoord.HasValue)
            {
                CommitSelection(_hover.HoveredCoord.Value, _hover.HoveredWorldPosition);
            }
        }

        private void ResolveInput()
        {
            if (_inputSource != null)
            {
                if (_inputSource is ICameraInput direct)
                {
                    _input = direct;
                    return;
                }
                // _inputSource may be a CameraInputInstaller (or any MonoBehaviour
                // sitting on the GameObject that holds the actual provider).
                ICameraInput sibling = _inputSource.GetComponent<ICameraInput>();
                if (sibling != null)
                {
                    _input = sibling;
                    return;
                }
            }
            _input = GetComponent<ICameraInput>();
        }

        private void EnsureBootstrapSubscription()
        {
            if (_subscribedToBootstrap || _bootstrap == null) return;
            _bootstrap.OnRegenerated += ClearSelectionOnRegenerate;
            _subscribedToBootstrap = true;
        }

        private void ClearSelectionOnRegenerate()
        {
            ClearSelection();
        }

        private void CommitSelection(HexCoord coord, Vector3 worldPosition)
        {
            bool changed = !_selectedCoord.HasValue || !_selectedCoord.Value.Equals(coord);
            _selectedCoord = coord;
            if (_outline != null) _outline.transform.position = worldPosition;
            SetOutlineVisible(true);
            if (changed) OnSelectionChanged?.Invoke(_selectedCoord);
        }

        private void ClearSelection()
        {
            bool changed = _selectedCoord.HasValue;
            _selectedCoord = null;
            SetOutlineVisible(false);
            if (changed) OnSelectionChanged?.Invoke(null);
        }

        private void SetOutlineVisible(bool visible)
        {
            if (_outline != null && _outline.activeSelf != visible)
            {
                _outline.SetActive(visible);
            }
        }
    }
}
