using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Payload.UI.Commands.Steam;

namespace ModManager.patches
{
    public static class ModLoadingPatches
    {

        [HarmonyPatch(typeof(UILoadingScreenModProgress), "Update")]
        private static class UpdateLoadingScreen
        {
            private static bool screenOverridden = false;

            // Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType, Vector4 border
            private static Sprite CloneSprite(Sprite sprite, Texture2D newTex)
            {
                Sprite newSprite = Sprite.Create(newTex, sprite.rect, sprite.pivot, sprite.pixelsPerUnit, 10, SpriteMeshType.Tight, sprite.border);
                return newSprite;
            }

            private static Vector2[] GetCorrectedSpriteVertices(Sprite sprite)
            {
                Vector2[] spriteVertices = sprite.vertices;
                for (int i = 0; i < spriteVertices.Length; i++)
                {
                    spriteVertices[i].x = Mathf.Clamp(
                        (sprite.vertices[i].x - sprite.bounds.center.x -
                            (sprite.textureRectOffset.x / sprite.texture.width) + sprite.bounds.extents.x) /
                        (2.0f * sprite.bounds.extents.x) * sprite.rect.width,
                        0.0f, sprite.rect.width);

                    spriteVertices[i].y = Mathf.Clamp(
                        (sprite.vertices[i].y - sprite.bounds.center.y -
                            (sprite.textureRectOffset.y / sprite.texture.height) + sprite.bounds.extents.y) /
                        (2.0f * sprite.bounds.extents.y) * sprite.rect.height,
                        0.0f, sprite.rect.height);
                }
                return spriteVertices;
            }

            [HarmonyPrefix]
            public static void Prefix(UILoadingScreenModProgress __instance)
            {
                if (!screenOverridden)
                {

                    ModContainer container = ModManager.ModManagerContainer;
                    ModManager.logger.Debug("✔️ Got ModContainer");
                    ModContents contents = container.Contents;
                    ModManager.logger.Debug("🗃️ Got ModContents");

                    if (contents != null)
                    {
                        screenOverridden = true;
                        Transform text = __instance.loadingBar.transform.GetChild(0);
                        Transform Background = __instance.loadingBar.transform.GetChild(1);
                        Transform Fill = Background.GetChild(0);
                        Image background = Background.GetComponent<Image>();
                        Image fill = Fill.GetComponent<Image>();

                        Texture2D ttsmmIcon = contents.FindAsset("ttsmm") as Texture2D;
                        Transform Icon = GameObject.Instantiate(Fill);
                        Icon.transform.parent = __instance.loadingBar.transform;
                        Icon.transform.localPosition = new Vector3(0, 100.0f, 0);
                        Icon.transform.localScale = Vector3.one;
                        Image icon = Icon.GetComponent<Image>();

                        Sprite iconSprite = Sprite.Create(ttsmmIcon, new Rect(0, 0, ttsmmIcon.width, ttsmmIcon.height), new Vector2(0.5f, 0.5f), fill.sprite.pixelsPerUnit, 10);
                        icon.sprite = iconSprite;
                        icon.fillAmount = 1.0f;
                        icon.SetNativeSize();

                        // Resolution resolution = Screen.currentResolution;

                        text.localPosition = text.localPosition + 200 * Vector3.down;
                        Background.localPosition = Background.localPosition + 200 * Vector3.down;

                        ModManager.logger.Debug("🖼️ Got target images");
                        Texture2D fillTex = contents.FindAsset("fill") as Texture2D;
                        Texture2D backgroundTex = contents.FindAsset("background") as Texture2D;
                        ModManager.logger.Debug("🎨 Got Replacement Textures");

                        Sprite newFill = CloneSprite(fill.sprite, fillTex);
                        Sprite newBackground = CloneSprite(background.sprite, backgroundTex);

                        // Override background geometry
                        Vector2[] backgroundVertices = new Vector2[] {
                            new Vector2(2.99f, 0.22f),
                            new Vector2(3.0f, -0.19f),
                            new Vector2(3.0f, 0.19f),
                            new Vector2(2.99f, -0.22f),
                            new Vector2(2.97f, 0.25f),
                            new Vector2(2.96f, -0.25f),
                            new Vector2(-2.96f, 0.25f),
                            new Vector2(-2.96f, -0.25f),
                            new Vector2(-3.0f, 0.21f),
                            new Vector2(-3.0f, -0.23f)
                        };
                        ushort[] backgroundTriangles = new ushort[] { 9, 8, 7, 6, 7, 8, 5, 7, 6, 4, 5, 6, 3, 5, 4, 0, 3, 4, 1, 3, 0, 2, 1, 0 };
                        newBackground.OverrideGeometry(GetCorrectedSpriteVertices(background.sprite), background.sprite.triangles);

                        Vector2[] fillVertices = new Vector2[] {
                            new Vector2(-0.25f, 0.25f),
                            new Vector2(0.25f, -0.25f),
                            new Vector2(0.25f, 0.25f),
                            new Vector2(-0.25f, -0.25f)
                        };
                        ushort[] fillTriangles = new ushort[] { 3, 0, 1, 2, 1, 0 };
                        newFill.OverrideGeometry(GetCorrectedSpriteVertices(fill.sprite), fill.sprite.triangles);

                        Sprite oldFill = fill.sprite;
                        Sprite oldBackground = background.sprite;
                        fill.sprite = newFill;
                        background.sprite = newBackground;

                        // fill.sprite = oldFill;
                        // background.sprite = oldBackground;

                        // fill.material.SetTexture("_MainTex", fillTex);
                       //  background.material.SetTexture("_MainTex", backgroundTex);
                        // background.color = new Color(0.3f, 0.0f, 1.0f);
                        // fill.color = new Color(1.0f, 0.0f, 0.7f);
                    }
                }
            }
        }

        // Notes on mod loading process:
        // Mod ID is enforced unique string based on key inside ManMods.m_Mods
        // *Every* local mod is always processed, and while content may be removed, the ModContainer and key remain in m_Mods
        // Workshop mods do not initially exist inside m_Mods, but will attempt to add themselves after they are downloaded.
        // This has the side effect that any Local mod will *always* override the corresponding workshop item

        // In base game, only one ModBase is allowed per ModContainer, and it will always be the last one present.
        // This allows for a kind of "ghetto compatibility", where multiple different mod .dlls are present, but only the last one will be used.
        // So, if you want your mod to have additional features if another mod is also used, then you can output two .dlls - one without the extra features, and one with them
        //  - In case user does not have the required .dll for extra features, the .dll without them will be loaded, whereas the other one will fail to load, and nothing will happen
        //  - In case user does, if filename of the one without appears later in ASCII-betical order (or whatever order Directory.GetParent(container.AssetBundlePath).EnumerateFiles() uses),
        //    then both will be loaded, but only the one that appears later will have its Init, EarlyInit, and DeInit hooks called.
        //    Note that this does not solve type collisions, where something like NuterraSteam tries to find an appropriate module based on type name

        // This particular feature is no longer possible when using this mod, as we explicitly attempt to load every single .dll, and allow for multiple ModBase objects per ModContainer
        // TODO: implement .json config handling to replicate that (we can configure it to only load certain .dll files, based on a set of criteria that are met)

        /// <summary>
        /// Replace ProcessLoadingMod with our own IENumerator, where we don't load assembly files. We will handle all of that later, once all .dlls are guaranteed loaded
        /// </summary>
        /// <remarks>
        /// Has the desired side effect that modContainer.Script and modContents.script will not be set, so only we will be called via DeInitModScripts and EarlyInit. EarlyInit does nothing, and InitModScripts has been overriden by us
        /// </remarks>
        [HarmonyPatch(typeof(ManMods), "ProcessLoadingMod")]
        public static class PatchLoadingAssembly
        {
            // Basic replacement code taken from this Harmony example: https://gist.github.com/pardeike/c873b95e983e4814a8f6eb522329aee5
            class CustomEnumerator : IEnumerable<float>
            {
                public ModContainer container;

                private float Scale(float subProgress, float lower, float upper)
                {
                    return Mathf.Lerp(lower, upper, subProgress);
                }

                public IEnumerator<float> GetEnumerator()
                {
                    AssetBundleCreateRequest createRequest = AssetBundle.LoadFromFileAsync(container.AssetBundlePath);
                    while (!createRequest.isDone)
                    {
                        yield return this.Scale(createRequest.progress, 0f, 0.2f);
                    }
                    AssetBundle bundle = createRequest.assetBundle;
                    if (bundle == null)
                    {
                        ModManager.logger.Error("Load AssetBundle at path {Path} failed for mod {Mod}", container.AssetBundlePath, container.ModID);
                        container.OnLoadFailed();
                        yield return 1f;
                    }
                    AssetBundleRequest loadRequest = bundle.LoadAssetAsync<ModContents>("Contents.asset");
                    while (!loadRequest.isDone)
                    {
                        yield return this.Scale(loadRequest.progress, 0.25f, 0.4f);
                    }
                    ModContents contents = loadRequest.asset as ModContents;
                    if (contents == null)
                    {
                        ModManager.logger.Error("Load AssetBundle Contents.asset failed for mod {Mod}", container.ModID);
                        container.OnLoadFailed();
                        yield return 1f;
                    }
                    if (contents.m_Corps.Count > 0)
                    {
                        int i;
                        for (int corpIndex = 0; corpIndex < contents.m_Corps.Count; corpIndex = i + 1)
                        {
                            container.RegisterAsset(contents.m_Corps[corpIndex]);
                            yield return this.Scale((float)(corpIndex / contents.m_Corps.Count), 0.4f, 0.5f);
                            i = corpIndex;
                        }
                    }
                    if (contents.m_Skins.Count > 0)
                    {
                        int i;
                        for (int corpIndex = 0; corpIndex < contents.m_Skins.Count; corpIndex = i + 1)
                        {
                            container.RegisterAsset(contents.m_Skins[corpIndex]);
                            yield return this.Scale((float)(corpIndex / contents.m_Skins.Count), 0.5f, 0.75f);
                            i = corpIndex;
                        }
                    }
                    if (contents.m_Blocks.Count > 0)
                    {
                        int i;
                        for (int corpIndex = 0; corpIndex < contents.m_Blocks.Count; corpIndex = i + 1)
                        {
                            container.RegisterAsset(contents.m_Blocks[corpIndex]);
                            yield return this.Scale((float)(corpIndex / contents.m_Blocks.Count), 0.6f, 0.8f);
                            i = corpIndex;
                        }
                    }
                    yield return 0.9f;
                    container.OnLoadComplete(contents);
                    yield return 1f;
                    yield break;
                }

                IEnumerator<float> IEnumerable<float>.GetEnumerator()
                {
                    return GetEnumerator();
                }

                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                {
                    return GetEnumerator();
                }
            }

            [HarmonyPostfix]
            static void Postfix(ref IEnumerator<float> __result, ModContainer container)
            {
                var myEnumerator = new CustomEnumerator()
                {
                    container = container
                };
                __result = myEnumerator.GetEnumerator();
            }
        }

        // Patch workshop loading to make sure all dependencies are loaded first
        // Also use our workflow to circumvent callback missed errors. Can only have it fail on first load now.
        [HarmonyPatch(typeof(ManMods), "LoadWorkshopData")]
        public static class PatchWorkshopLoad
        {
            [HarmonyPrefix]
            public static bool Prefix(SteamDownloadItemData item, bool remote)
            {
                WorkshopLoader.LoadWorkshopMod(item, remote);
                return false;
            }
        }
    }
}
