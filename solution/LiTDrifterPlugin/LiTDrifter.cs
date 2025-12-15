using BepInEx;
using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using System.Security.Permissions;
using BepInEx.Configuration;
using MonoMod.RuntimeDetour.HookGen;
using On.RoR2.Navigation;
using RoR2.ContentManagement;
using UnityEngine.AddressableAssets;
using RoR2.Projectile;
using TestMod;
using SurvivorCatalog = On.RoR2.SurvivorCatalog;


#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete
namespace LiTDrifter
{
    [BepInPlugin("com.icebro.LiTDrifter","LiTDrifter","1.0.1")]
    public partial class LiTDrifterPlugin : BaseUnityPlugin
    {
        internal static LiTDrifterPlugin Instance { get; private set; }
        internal static ManualLogSource InstanceLogger => Instance?.Logger;
        
        private static AssetBundle assetBundle;
        private static readonly List<Material> materialsWithRoRShader = new List<Material>();
        private ConfigEntry<bool> replaceDrifter;
        private static SkinDef drifterSkin;
        private void Start()
        {
            Instance = this;

            BeforeStart();

            assetBundle = AssetBundle.LoadFromFile(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Info.Location), "LiTDrifter.icebrolitdrifter"));
            
            BodyCatalog.availability.CallWhenAvailable(BodyCatalogInit);
            On.RoR2.SurvivorCatalog.SetSurvivorDefs += SurvivorCatalogOnSetSurvivorDefs;
            
            HookEndpointManager.Add(typeof(Language).GetMethod(nameof(Language.LoadStrings)), (Action<Action<Language>, Language>)LanguageLoadStrings);

            ReplaceShaders();

            AfterStart();
        }

        private void SurvivorCatalogOnSetSurvivorDefs(SurvivorCatalog.orig_SetSurvivorDefs orig, SurvivorDef[] newSurvivorDefs)
        {
            orig(newSurvivorDefs);

            if (replaceDrifter.Value)
            {
                SurvivorDef drifterDef = RoR2.SurvivorCatalog.FindSurvivorDefFromBody(BodyCatalog.FindBodyPrefab("DrifterBody"));
                ModelSkinController drifterSkinController = drifterDef.displayPrefab.transform.Find("mdlDrifter").GetComponent<ModelSkinController>();
                drifterSkinController.skins[0] = drifterSkin;
            }
        }

        void BeforeStart()
        {
            replaceDrifter = Config.Bind("Lost in Transit Drifter", 
                "Replace drifter", 
                false,
                "replaces default drifter skin and surivor icon !!");
        }
        
        partial void AfterStart();
        static partial void BeforeBodyCatalogInit();
        static partial void AfterBodyCatalogInit();

        private static void ReplaceShaders()
        {
            LoadMaterialsWithReplacedShader(@"RoR2/Base/Shaders/HGStandard.shader"
                ,@"Assets/characterbodyexample/LiTDrifterShader.mat");

        }

        private static void LoadMaterialsWithReplacedShader(string shaderPath, params string[] materialPaths)
        {
            var shader = Addressables.LoadAssetAsync<Shader>(shaderPath).WaitForCompletion();
            foreach (var materialPath in materialPaths)
            {
                var material = assetBundle.LoadAsset<Material>(materialPath);
                material.shader = shader;
                materialsWithRoRShader.Add(material);
            }
        }

        private static void LanguageLoadStrings(Action<Language> orig, Language self)
        {
            orig(self);
            
            self.SetStringByToken("ICEBRO_SKIN_LITDRIFTER_NAME", "Lost in Transit");
            self.SetStringByToken("ICEBRO_SKIN_LITDRIFTER_DESC", "man ,,.,. looks like we're ,..,.,.,,. lost in transit or something ,..,.,.,,,.., ");
        }

        private void BodyCatalogInit()
        {
            BeforeBodyCatalogInit();

            if (replaceDrifter.Value)
            {
                GameObject body = BodyCatalog.FindBodyPrefab("DrifterBody");
                CharacterBody characterBody = body.GetComponent<CharacterBody>();
                characterBody.portraitIcon = assetBundle.LoadAsset<Sprite>(@"Assets/characterbodyexample/drifter_icon.png").texture;
            }
            
            
            AddDrifterBodyLiTDrifterSkin();
            
            
            AfterBodyCatalogInit();
        }

        static partial void DrifterBodyLiTDrifterSkinAdded(SkinDef skinDef, GameObject bodyPrefab);

        private void AddDrifterBodyLiTDrifterSkin()
        {
            var bodyName = "DrifterBody";
            var skinName = "LiTDrifter";
            try
            {
                var bodyPrefab = BodyCatalog.FindBodyPrefab(bodyName);
                if (!bodyPrefab)
                {
                    InstanceLogger.LogWarning($"Failed to add \"{skinName}\" skin because \"{bodyName}\" doesn't exist");
                    return;
                }

                var modelLocator = bodyPrefab.GetComponent<ModelLocator>();
                if (!modelLocator)
                {
                    InstanceLogger.LogWarning($"Failed to add \"{skinName}\" skin to \"{bodyName}\" because it doesn't have \"ModelLocator\" component");
                    return;
                }

                var mdl = modelLocator.modelTransform.gameObject;
                var skinController = mdl ? mdl.GetComponent<ModelSkinController>() : null;
                if (!skinController)
                {
                    InstanceLogger.LogWarning($"Failed to add \"{skinName}\" skin to \"{bodyName}\" because it doesn't have \"ModelSkinController\" component");
                    return;
                }

                var renderers = mdl.GetComponentsInChildren<Renderer>(true);
                var lights = mdl.GetComponentsInChildren<Light>(true);

                var skin = ScriptableObject.CreateInstance<SkinDef>();
                var skinParams = ScriptableObject.CreateInstance<SkinDefParams>();
                skin.skinDefParams = skinParams;

                TryCatchThrow("Icon", () =>
                {
                    skin.icon = assetBundle.LoadAsset<Sprite>(@"Assets/characterbodyexample/Default_Drifter.png");
                });
                skin.name = skinName;
                skin.nameToken = "ICEBRO_SKIN_LITDRIFTER_NAME";
                skin.rootObject = mdl;
                TryCatchThrow("Base Skins", () =>
                {
                    skin.baseSkins =
                    [
                        ThrowIfOutOfBounds(0, "Index 0 is out of bounds of skins array", skinController.skins, 0)
                    ];
                });
                TryCatchThrow("Renderer Infos", () =>
                {
                    skinParams.rendererInfos = new CharacterModel.RendererInfo[]
                    {
                        new CharacterModel.RendererInfo
                        {
                            defaultMaterial = assetBundle.LoadAsset<Material>(@"Assets/characterbodyexample/LiTDrifterShader.mat"),
                            defaultShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                            ignoreOverlays = false,
                            renderer = ThrowIfNull(0, "There is no renderer with the name \"meshDrifter\"", renderers.FirstOrDefault(r => r.name == "meshDrifter")),
                        },
                        new CharacterModel.RendererInfo
                        {
                            defaultMaterial = assetBundle.LoadAsset<Material>(@"Assets/characterbodyexample/LiTDrifterShader.mat"),
                            defaultShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                            ignoreOverlays = false,
                            renderer = ThrowIfNull(1, "There is no renderer with the name \"meshBag\"", renderers.FirstOrDefault(r => r.name == "meshBag")),
                        },
                    };
                });
                TryCatchThrow("Mesh Replacements", () =>
                {
                    skinParams.meshReplacements = new SkinDefParams.MeshReplacement[]
                    {
                        new SkinDefParams.MeshReplacement
                        {
                            mesh = assetBundle.LoadAsset<Mesh>(@"Assets/SkinMods/LiTDrifter/Meshes/LITDrifterBody.mesh"),
                            renderer = ThrowIfNull(0, "There is no renderer with the name \"meshDrifter\"", renderers.FirstOrDefault(r => r.name == "meshDrifter")),
                        },
                        new SkinDefParams.MeshReplacement
                        {
                            mesh = assetBundle.LoadAsset<Mesh>(@"Assets/SkinMods/LiTDrifter/Meshes/LITDrifterBag.mesh"),
                            renderer = ThrowIfNull(1, "There is no renderer with the name \"meshBag\"", renderers.FirstOrDefault(r => r.name == "meshBag")),
                        },
                    };
                });

                if (replaceDrifter.Value)
                {
                    skinController.skins[0] = skin;
                    drifterSkin = skin;
                    //SkinCatalog.skinsByBody[(int)bodyPrefab.GetComponent<CharacterBody>().bodyIndex] = skinController.skins;
                }
                else
                {
                    Array.Resize(ref skinController.skins, skinController.skins.Length + 1);
                    skinController.skins[^1] = skin;
                }
                

                DrifterBodyLiTDrifterSkinAdded(skin, bodyPrefab);
            }
            catch (FieldException e)
            {
                if (e.InnerException is ElementException ie)
                {
					InstanceLogger.LogWarning($"Failed to add \"{skinName}\" skin to \"{bodyName}\"");
					InstanceLogger.LogWarning($"Field causing issue: {e.Message}, element: {ie.Index}");
					InstanceLogger.LogWarning(ie.Message);
					InstanceLogger.LogError(e.InnerException);
                }
                else
                {
					InstanceLogger.LogWarning($"Failed to add \"{skinName}\" skin to \"{bodyName}\"");
					InstanceLogger.LogWarning($"Field causing issue: {e.Message}");
					InstanceLogger.LogError(e.InnerException);
                }
            }
            catch (Exception e)
            {
                InstanceLogger.LogWarning($"Failed to add \"{skinName}\" skin to \"{bodyName}\"");
                InstanceLogger.LogError(e);
            }
        }

        private static T ThrowIfEquals<T>(int index, string message, T value, T expected) where T: Enum
        {
            if (value.Equals(expected))
            {
                throw new ElementException(index, message);
            }

            return value;
        }
        
        private static T ThrowIfOutOfBounds<T>(int index, string message, T[] array, int elementIndex) where T: class
        {
            if (array is null || array.Length <= elementIndex)
            {
                throw new ElementException(index, message);
            }

            return array[elementIndex];
        }

        private static T ThrowIfNull<T>(int index, string message, T value) where T: class
        {
            if (value is null)
            {
                throw new ElementException(index, message);
            }

            return value;
        }

        private static void TryCatchThrow(string message, Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                throw new FieldException(message, e);
            }
        }
        
        private static void TryAddComponent<T>(GameObject obj) where T : Component
        {
            if (obj && !obj.GetComponent<T>())
            {
                obj.AddComponent<T>();
            }
        }

        private class FieldException : Exception
        {
            public FieldException(string message, Exception innerException) : base(message, innerException) { }
        }

        private class ElementException : Exception
        {
            public int Index { get; }
            public ElementException(int index, string message) : base(message)
            {
                Index = index;
            }
        }
    }
}