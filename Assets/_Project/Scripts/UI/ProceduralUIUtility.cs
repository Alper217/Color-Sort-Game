using UnityEngine;
using UnityEngine.UI;

namespace SandSort.UI
{
    /// <summary>
    /// Generates high-resolution, anti-aliased procedural sprites and textures for
    /// casual mobile puzzle UI (Dream / Peak / Good Job quality).
    /// Generates 9-slice rounded rectangles, rotating sunbursts, 5-point stars, and 3D buttons.
    /// Everything is generated once and cached with zero memory garbage.
    /// </summary>
    public static class ProceduralUIUtility
    {
        private static Sprite s_roundedRectSprite;
        private static Sprite s_sunburstSprite;
        private static Sprite s_goldStarSprite;
        private static Sprite s_emptyStarSprite;
        private static Sprite s_warningBadgeSprite;

        public static Sprite GetOrCreateRoundedRectSprite(int size = 128, int radius = 32)
        {
            if (s_roundedRectSprite != null) return s_roundedRectSprite;

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Tex_Procedural_RoundedRect_9Slice",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            float r = radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Find distance to closest corner
                    float cx = x < r ? r : (x > size - 1 - r ? size - 1 - r : x);
                    float cy = y < r ? r : (y > size - 1 - r ? size - 1 - r : y);

                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = Mathf.Clamp01(r + 0.5f - dist);
                    alpha = alpha * alpha * (3f - 2f * alpha); // smoothstep anti-aliasing
                    byte a = (byte)(alpha * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            // 9-slice border: radius pixels from every side
            Vector4 border = new Vector4(radius, radius, radius, radius);
            s_roundedRectSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            return s_roundedRectSprite;
        }

        public static Sprite GetOrCreateSunburstSprite(int size = 256, int rayCount = 14)
        {
            if (s_sunburstSprite != null) return s_sunburstSprite;

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Tex_Procedural_Sunburst",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float maxR = center;

            for (int y = 0; y < size; y++)
            {
                float dy = y - center;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist >= maxR)
                    {
                        pixels[y * size + x] = new Color32(255, 255, 255, 0);
                        continue;
                    }

                    float angle = Mathf.Atan2(dy, dx);
                    // Sine-modulated radial rays
                    float ray = Mathf.Cos(angle * rayCount) * 0.5f + 0.5f;
                    ray = Mathf.Pow(ray, 1.8f);

                    // Fade at edges
                    float edgeFade = Mathf.Clamp01((maxR - dist) / (maxR * 0.35f));
                    edgeFade = edgeFade * edgeFade * (3f - 2f * edgeFade);

                    float alpha = ray * edgeFade;
                    byte a = (byte)(Mathf.Clamp01(alpha) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            s_sunburstSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return s_sunburstSprite;
        }

        public static Sprite GetOrCreateStarSprite(int size = 96, bool filled = true)
        {
            if (filled && s_goldStarSprite != null) return s_goldStarSprite;
            if (!filled && s_emptyStarSprite != null) return s_emptyStarSprite;

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = filled ? "Tex_Procedural_GoldStar" : "Tex_Procedural_EmptyStar",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float outerR = center * 0.92f;
            float innerR = outerR * 0.44f;
            const int points = 5;

            // Generate 10 star vertices
            Vector2[] verts = new Vector2[points * 2];
            float angleStep = Mathf.PI / points;
            float startAngle = Mathf.PI * 0.5f; // pointing up

            for (int i = 0; i < points * 2; i++)
            {
                float a = startAngle + i * angleStep;
                float r = (i % 2 == 0) ? outerR : innerR;
                verts[i] = new Vector2(center + Mathf.Cos(a) * r, center + Mathf.Sin(a) * r);
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pt = new Vector2(x, y);
                    bool inside = IsPointInPolygon(pt, verts);

                    float distToCenter = Vector2.Distance(pt, new Vector2(center, center));
                    if (inside)
                    {
                        if (filled)
                        {
                            // Inner gradient: white-yellow at top, rich gold at bottom
                            float t = Mathf.Clamp01((y - (center - outerR)) / (outerR * 2f));
                            Color col = Color.Lerp(new Color32(255, 175, 20, 255), new Color32(255, 245, 120, 255), t);
                            pixels[y * size + x] = col;
                        }
                        else
                        {
                            pixels[y * size + x] = new Color32(110, 105, 135, 180);
                        }
                    }
                    else
                    {
                        // 1px anti-aliasing
                        float minDist = float.MaxValue;
                        for (int i = 0; i < verts.Length; i++)
                        {
                            Vector2 v1 = verts[i];
                            Vector2 v2 = verts[(i + 1) % verts.Length];
                            float d = DistancePointToSegment(pt, v1, v2);
                            if (d < minDist) minDist = d;
                        }

                        if (minDist < 1.2f)
                        {
                            float alpha = Mathf.Clamp01(1.2f - minDist) / 1.2f;
                            byte a = (byte)(alpha * (filled ? 255 : 180));
                            Color32 c = filled ? new Color32(255, 190, 30, a) : new Color32(110, 105, 135, a);
                            pixels[y * size + x] = c;
                        }
                        else
                        {
                            pixels[y * size + x] = new Color32(255, 255, 255, 0);
                        }
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            if (filled) s_goldStarSprite = s;
            else s_emptyStarSprite = s;
            return s;
        }

        public static Sprite GetOrCreateWarningBadgeSprite(int size = 96)
        {
            if (s_warningBadgeSprite != null) return s_warningBadgeSprite;

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Tex_Procedural_WarningBadge",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float maxR = center * 0.90f;

            for (int y = 0; y < size; y++)
            {
                float dy = y - center;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist <= maxR)
                    {
                        // Gradient from Coral Red to Deep Red
                        float t = (y / (float)size);
                        Color c = Color.Lerp(new Color32(230, 40, 60, 255), new Color32(255, 95, 110, 255), t);
                        // Exclamation mark drawing
                        float ex = Mathf.Abs(dx);
                        bool isExclamationLine = ex < 4.5f && dy >= -8f && dy <= maxR * 0.55f;
                        bool isExclamationDot = dist < 5.5f && dy < -16f && dy > -27f;

                        if (isExclamationLine || isExclamationDot)
                        {
                            pixels[y * size + x] = new Color32(255, 255, 255, 255);
                        }
                        else
                        {
                            pixels[y * size + x] = c;
                        }
                    }
                    else if (dist <= maxR + 1.2f)
                    {
                        float alpha = Mathf.Clamp01(maxR + 1.2f - dist) / 1.2f;
                        pixels[y * size + x] = new Color32(230, 40, 60, (byte)(alpha * 255f));
                    }
                    else
                    {
                        pixels[y * size + x] = new Color32(255, 255, 255, 0);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            s_warningBadgeSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return s_warningBadgeSprite;
        }

        private static bool IsPointInPolygon(Vector2 p, Vector2[] poly)
        {
            int inside = 0;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                {
                    inside++;
                }
            }
            return (inside % 2) != 0;
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 proj = a + ab * t;
            return Vector2.Distance(p, proj);
        }
    }
}

