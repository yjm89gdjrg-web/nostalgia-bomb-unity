using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NostalgiaBomb.Editor
{
    public static class PrototypeMenus
    {
        public const string ScenePath="Assets/Scenes/Prototype.unity";
        [MenuItem("Nostalgia Bomb/Create or refresh prototype scene")]
        public static void CreateScene()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("NostalgiaBomb Bootstrap").AddComponent<MatchGame>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            ConfigureIOS();
            AssetDatabase.SaveAssets();
            Debug.Log("Saved procedural bootstrap scene; open it and press Play. No baked assets required.");
        }
        [MenuItem("Nostalgia Bomb/Configure iPhone landscape")]
        public static void ConfigureIOS()
        {
            // 0 = legacy Input Manager. Serialized field is not exposed as a public PlayerSettings property.
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input=settings.FindProperty("activeInputHandler");
            if(input==null) throw new System.InvalidOperationException("Cannot find activeInputHandler in this editor.");
            input.intValue=0; settings.ApplyModifiedProperties();
            PlayerSettings.companyName="Original Prototypes";
            PlayerSettings.productName="Nostalgia Bomb Greybox";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS,"com.originalprototypes.nostalgiabomb");
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait=false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;
            PlayerSettings.allowedAutorotateToLandscapeLeft=true;
            PlayerSettings.allowedAutorotateToLandscapeRight=true;
            PlayerSettings.iOS.targetDevice=iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.targetOSVersionString="15.0";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS,ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS,1); // ARM64.
            // Signing credentials and developer team intentionally remain user-owned.
        }
        [MenuItem("Nostalgia Bomb/Export iOS Xcode project")]
        public static void ExportIOS()
        {
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS,BuildTarget.iOS))
            { EditorUtility.DisplayDialog("iOS module required","Install Unity's iOS Build Support through Unity Hub on your build machine.","OK"); return; }
            if(!File.Exists(ScenePath)) { CreateScene(); if(!File.Exists(ScenePath)) return; }
            ConfigureIOS();
            string output=EditorUtility.SaveFolderPanel("Export Xcode project","","NostalgiaBomb-iOS");
            if(string.IsNullOrEmpty(output)) return;
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.iOS,options=BuildOptions.None });
            if(report.summary.result!=BuildResult.Succeeded)
                throw new System.Exception("Unity iOS export failed: "+report.summary.result);
            Debug.Log("Xcode project exported. Not an IPA: build/sign/install with macOS + Xcode.");
        }
    }
}
