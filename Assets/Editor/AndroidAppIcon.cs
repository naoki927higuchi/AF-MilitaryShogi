using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Android;
using UnityEngine;

namespace MilitaryShogi.Editor
{
    // Rebuild launcher assets deterministically; keep the foreground inside adaptive masks.
    public static class AndroidAppIcon
    {
        private const string Folder = "Assets/Generated/Icons/Android";
        public static void Apply()
        {
            Directory.CreateDirectory(Folder);
            const int size = 512;
            var background = new Color32(22, 42, 57, 255);
            source = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Generated/Icons/app_icon_1024.png"); if (source == null) throw new InvalidOperationException("Missing shogi icon"); background = new Color32(35, 24, 16, 255);
            var legacy = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var foreground = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var back = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size, v = (y + .5f) / size;
                legacy.SetPixel(x, y, Sample(u, v));
                float fu = (u - .5f) / .62f + .5f, fv = (v - .5f) / .62f + .5f;
                foreground.SetPixel(x, y, fu >= 0 && fu <= 1 && fv >= 0 && fv <= 1 ? Sample(fu, fv) : Color.clear);
                back.SetPixel(x, y, background);
            }
            var legacyAsset = Save("launcher", legacy);
            var frontAsset = Save("foreground", foreground);
            var backAsset = Save("background", back);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { legacyAsset }, IconKind.Any);
            foreach (var kind in new[] { AndroidPlatformIconKind.Adaptive })
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var icon in icons)
                {
                    if (kind == AndroidPlatformIconKind.Adaptive) icon.SetTextures(new[] { backAsset, frontAsset });
                    else icon.SetTexture(legacyAsset);
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
                foreach (var icon in PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind))
                    for (int layer = 0; layer < icon.minLayerCount; layer++)
                        if (icon.GetTexture(layer) == null) throw new InvalidOperationException("Missing Android icon layer");
                Debug.Log("ANDROID_ICON_SET=" + kind + " slots=" + icons.Length);
            }
            AssetDatabase.SaveAssets();
        }
        private static Texture2D Save(string name, Texture2D texture)
        {
            string path = Folder + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        private static Texture2D source; private static Color Sample(float u, float v) { return source.GetPixelBilinear(u, v); }
    }
}