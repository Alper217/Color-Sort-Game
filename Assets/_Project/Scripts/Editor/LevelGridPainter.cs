using System;
using System.Collections.Generic;
using SandSort.Data;
using UnityEditor;
using UnityEngine;

namespace SandSort.EditorTools
{
    public enum PaintTool
    {
        Brush,
        Line,
        FloodFill,
        ColorPicker
    }

    /// <summary>
    /// Interactive pixel canvas painter for level design:
    /// Handles brush painting, line drawing, flood fill, color picking, undo/redo history,
    /// and generates real-time preview textures for the matrix display.
    /// </summary>
    public sealed class LevelGridPainter
    {
        public PaintTool ActiveTool = PaintTool.Brush;
        public int BrushSize = 1;
        public int PaintColorId = 0;
        public bool ShowGridLines = true;

        private readonly List<int[]> _undoStack = new List<int[]>();
        public const int MaxUndo = 30;

        private bool _isDragging;
        private Vector2Int _lastPaintedCell = new Vector2Int(-1, -1);
        private Vector2Int _lineStartCell = new Vector2Int(-1, -1);

        public int UndoCount => _undoStack.Count;

        public void PushUndo(LevelDataSO level)
        {
            if (level == null || level.GridCells == null) return;
            _undoStack.Add((int[])level.GridCells.Clone());
            if (_undoStack.Count > MaxUndo)
            {
                _undoStack.RemoveAt(0);
            }
        }

        public bool UndoPaint(LevelDataSO level)
        {
            if (_undoStack.Count == 0 || level == null) return false;
            int[] snap = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            if (level.GridCells == null || snap.Length != level.GridCells.Length) return false;
            level.GridCells = snap;
            return true;
        }

        public void ClearUndo()
        {
            _undoStack.Clear();
        }

        public void NewBlankCanvas(LevelDataSO level, int width, int height, int fillBgColorId)
        {
            if (level == null) return;
            PushUndo(level);
            level.Width = width;
            level.Height = height;
            level.EnsureGridAllocated();
            int fill = Mathf.Max(0, fillBgColorId);
            for (int i = 0; i < level.GridCells.Length; i++)
            {
                level.GridCells[i] = fill;
            }
        }

        public void PaintBrush(LevelDataSO level, Vector2Int center, int brushSize, int colorId)
        {
            if (level == null || level.GridCells == null) return;
            int w = level.Width;
            int h = level.Height;
            int r = Mathf.Max(0, brushSize - 1);

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (r > 1 && (dx * dx + dy * dy > r * r + 1)) continue;
                    int px = center.x + dx;
                    int py = center.y + dy;
                    if (px >= 0 && px < w && py >= 0 && py < h)
                    {
                        level.GridCells[py * w + px] = colorId;
                    }
                }
            }
        }

        public void PaintLine(LevelDataSO level, Vector2Int a, Vector2Int b, int brushSize, int colorId)
        {
            int dx = Mathf.Abs(b.x - a.x);
            int dy = Mathf.Abs(b.y - a.y);
            int sx = a.x < b.x ? 1 : -1;
            int sy = a.y < b.y ? 1 : -1;
            int err = dx - dy;

            Vector2Int cur = a;
            while (true)
            {
                PaintBrush(level, cur, brushSize, colorId);
                if (cur == b) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; cur.x += sx; }
                if (e2 < dx) { err += dx; cur.y += sy; }
            }
        }

        public void FloodFill(LevelDataSO level, Vector2Int start, int targetColorId)
        {
            if (level == null || level.GridCells == null) return;
            int w = level.Width;
            int h = level.Height;
            if (start.x < 0 || start.x >= w || start.y < 0 || start.y >= h) return;

            int sourceColor = level.GetCell(start.x, start.y);
            if (sourceColor == targetColorId) return;

            Queue<Vector2Int> q = new Queue<Vector2Int>();
            HashSet<int> visited = new HashSet<int>();
            q.Enqueue(start);
            visited.Add(start.y * w + start.x);

            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();
                level.GridCells[c.y * w + c.x] = targetColorId;

                Vector2Int[] neighbors = {
                    new Vector2Int(c.x + 1, c.y),
                    new Vector2Int(c.x - 1, c.y),
                    new Vector2Int(c.x, c.y + 1),
                    new Vector2Int(c.x, c.y - 1)
                };

                foreach (var n in neighbors)
                {
                    if (n.x >= 0 && n.x < w && n.y >= 0 && n.y < h)
                    {
                        int idx = n.y * w + n.x;
                        if (!visited.Contains(idx) && level.GridCells[idx] == sourceColor)
                        {
                            visited.Add(idx);
                            q.Enqueue(n);
                        }
                    }
                }
            }
        }

        public int PickColorAt(LevelDataSO level, Vector2Int cell)
        {
            if (level == null) return -1;
            int c = level.GetCell(cell.x, cell.y);
            if (c >= 0)
            {
                PaintColorId = c;
            }
            return c;
        }

        /// <summary>
        /// Handles mouse paint / draw events within the interactive matrix preview rect.
        /// </summary>
        public bool HandlePainting(Event e, Rect drawRect, LevelDataSO level, int paletteCount, Action onDirty)
        {
            if (level == null || level.GridCells == null) return false;
            int w = level.Width;
            int h = level.Height;
            if (w <= 0 || h <= 0) return false;

            int activeColor = Mathf.Clamp(PaintColorId, 0, Mathf.Max(0, paletteCount - 1));

            Vector2 mousePos = e.mousePosition;
            bool inside = drawRect.Contains(mousePos);

            float cellW = drawRect.width / w;
            float cellH = drawRect.height / h;

            Vector2 local = mousePos - drawRect.position;
            int cx = Mathf.Clamp((int)(local.x / cellW), 0, w - 1);
            int cy = Mathf.Clamp(h - 1 - (int)(local.y / cellH), 0, h - 1); // Row 0 is at bottom
            Vector2Int currentCell = new Vector2Int(cx, cy);

            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.MouseDown && e.button == 0 && inside)
            {
                GUIUtility.hotControl = controlId;
                _isDragging = true;
                PushUndo(level);

                if (ActiveTool == PaintTool.ColorPicker)
                {
                    PickColorAt(level, currentCell);
                    ActiveTool = PaintTool.Brush;
                }
                else if (ActiveTool == PaintTool.FloodFill)
                {
                    FloodFill(level, currentCell, activeColor);
                    onDirty?.Invoke();
                }
                else if (ActiveTool == PaintTool.Line)
                {
                    _lineStartCell = currentCell;
                }
                else
                {
                    PaintBrush(level, currentCell, BrushSize, activeColor);
                    _lastPaintedCell = currentCell;
                    onDirty?.Invoke();
                }

                e.Use();
                return true;
            }

            if (e.type == EventType.MouseDrag && e.button == 0 && _isDragging)
            {
                if (ActiveTool == PaintTool.Brush)
                {
                    if (_lastPaintedCell.x >= 0 && _lastPaintedCell != currentCell)
                    {
                        PaintLine(level, _lastPaintedCell, currentCell, BrushSize, activeColor);
                    }
                    else
                    {
                        PaintBrush(level, currentCell, BrushSize, activeColor);
                    }
                    _lastPaintedCell = currentCell;
                    onDirty?.Invoke();
                }
                else if (ActiveTool == PaintTool.ColorPicker)
                {
                    PickColorAt(level, currentCell);
                }

                e.Use();
                return true;
            }

            if (e.type == EventType.MouseUp && e.button == 0 && _isDragging)
            {
                GUIUtility.hotControl = 0;
                _isDragging = false;

                if (ActiveTool == PaintTool.Line && _lineStartCell.x >= 0)
                {
                    PaintLine(level, _lineStartCell, currentCell, BrushSize, activeColor);
                    _lineStartCell = new Vector2Int(-1, -1);
                    onDirty?.Invoke();
                }

                _lastPaintedCell = new Vector2Int(-1, -1);
                e.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Updates the Texture2D preview from the level's grid cells and palette.
        /// </summary>
        public static void UpdatePreviewTexture(LevelDataSO level, ref Texture2D previewTexture)
        {
            if (level == null || level.GridCells == null) return;
            int w = level.Width;
            int h = level.Height;
            if (w <= 0 || h <= 0) return;

            if (previewTexture == null || previewTexture.width != w || previewTexture.height != h)
            {
                if (previewTexture != null) UnityEngine.Object.DestroyImmediate(previewTexture);
                previewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            Color32[] px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int colorId = level.GetCell(x, y);
                    if (colorId >= 0 && level.Palette != null && colorId < level.Palette.Length)
                    {
                        px[y * w + x] = level.Palette[colorId];
                    }
                    else
                    {
                        bool checker = ((x + y) & 1) == 0;
                        px[y * w + x] = checker ? new Color32(30, 28, 38, 255) : new Color32(42, 38, 52, 255);
                    }
                }
            }

            previewTexture.SetPixels32(px);
            previewTexture.Apply();
        }
    }
}

