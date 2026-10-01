using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NostalgiaBomb.Editor
{
    public static class CiBuild
    {
        // GameCI buildMethod / Unity -executeMethod both invoke static methods with no arguments.
        public static void ExportIOS()
        {
            string output=Argument("-nostalgiaOutput");
            if(string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("Missing -nostalgiaOutput <directory>. This entry point exports Xcode, NOT IPA.");
            if(!File.Exists(PrototypeMenus.ScenePath))
                throw new FileNotFoundException("Committed prototype scene is missing.",PrototypeMenus.ScenePath);
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS,BuildTarget.iOS))
                throw new InvalidOperationException("Unity iOS Build Support is unavailable on this runner.");
            output=Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            PrototypeMenus.ConfigureIOS();
            AssetDatabase.SaveAssets();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes=new[]{PrototypeMenus.ScenePath},
                locationPathName=output,
                target=BuildTarget.iOS,
                options=BuildOptions.None
            });
            if(report.summary.result!=BuildResult.Succeeded)
                throw new InvalidOperationException("Unity Xcode export failed: "+report.summary.result+"; errors="+report.summary.totalErrors);
            if(!Directory.Exists(Path.Combine(output,"Unity-iPhone.xcodeproj")))
                throw new InvalidOperationException("Build reported success but Unity-iPhone.xcodeproj was not produced.");
            File.WriteAllText(Path.Combine(output,"EXPORT-ONLY-NOT-IPA.txt"),
                "Unity exported this Xcode project. No Xcode compilation, Apple signing, IPA creation or device testing has occurred.\n");
            Debug.Log("Xcode export succeeded at "+output+". NOT an IPA and NOT device-tested.");
        }
        static string Argument(string key)
        {
            string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length;i++)
                if(string.Equals(args[i],key,StringComparison.OrdinalIgnoreCase))
                {
                    if(i+1>=args.Length || args[i+1].StartsWith("-"))
                        throw new ArgumentException("Missing value for "+key);
                    return args[i+1];
                }
            return null;
        }
    }
}
