using UnityEditor;
using UnityEditor.Build.Profile;

//Only bother changing this in vscode, vs doesnt pick up intellisense for shit
namespace Kongroo
{
    public class BuildProfileScript
    {
        [MenuItem("Build/Build Windows")]
        public static void BuildWindows()
        {

            var specificBuildProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>(
                "Assets/Settings/Build Profiles/DevWindows.asset"
            );
            BuildProfile.SetActiveBuildProfile(specificBuildProfile);

            var options = new BuildPlayerWithProfileOptions
            {
                buildProfile = BuildProfile.GetActiveBuildProfile(),
                locationPathName = "WinBuild/Brackeys2026.exe"
            };

            BuildPipeline.BuildPlayer(options);
        }

        [MenuItem("Build/Build WebGL")]
        public static void BuildLocalWebGL()
        {
            var specificBuildProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>(
                "Assets/Settings/Build Profiles/LocalWebGL.asset"
            );
            BuildProfile.SetActiveBuildProfile(specificBuildProfile);

            var options = new BuildPlayerWithProfileOptions
            {
                buildProfile = BuildProfile.GetActiveBuildProfile(),
                locationPathName = "WebBuild"
            };

            BuildPipeline.BuildPlayer(options);
        }

        [MenuItem("Build/Build Linux")]
        public static void BuildLinuxServer()
        {
            var specificBuildProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>(
                "Assets/Settings/Build Profiles/LinuxServer.asset"
            );
            BuildProfile.SetActiveBuildProfile(specificBuildProfile);

            var options = new BuildPlayerWithProfileOptions
            {
                buildProfile = BuildProfile.GetActiveBuildProfile(),
                locationPathName = "LinuxBuild/out"
            };

            BuildPipeline.BuildPlayer(options);
        }


        [MenuItem("Build/Print Profile")]
        static void PrintBuildProfile()
        {
            var profiles = BuildProfile.GetActiveBuildProfile();

            UnityEngine.Debug.Log(profiles.ToString());

        }

        //static void BuildByName(string profileName)
        //{
        //    var profiles = BuildProfile.GetAllBuildProfiles();

        //    foreach (var profile in profiles)
        //    {
        //        if (profile.name == profileName)
        //        {
        //            BuildProfile.SetActiveBuildProfile(profile);

        //            BuildPipeline.BuildPlayer(
        //                EditorBuildSettings.scenes,
        //                profile.outputPath,
        //                profile.buildTarget,
        //                BuildOptions.None
        //            );

        //            return;
        //        }
        //    }

        //    UnityEngine.Debug.LogError("Build profile not found: " + profileName);
        //}
    }

}