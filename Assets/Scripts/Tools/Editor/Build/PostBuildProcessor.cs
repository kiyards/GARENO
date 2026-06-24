using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

public class PostBuildProcessor : IPostprocessBuildWithReport
{
    // Define the callback order; lower number means higher priority
    public int callbackOrder => 0;

    // This method is called after the build process is complete
    public void OnPostprocessBuild(BuildReport report)
    {
        // Define the source path of the steamappid.txt file
        string sourcePath = "steam_appid.txt";

        // Define the destination path based on the build target
        string destinationPath = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "steam_appid.txt");

        // Check if the source file exists
        if (File.Exists(sourcePath))
        {
            // Copy the steamappid.txt file to the build directory
            File.Copy(sourcePath, destinationPath, true);
            Debug.Log($"steam_appid.txt copied to {destinationPath}");
        }
        else
        {
            Debug.LogWarning($"steam_appid.txt not found at {sourcePath}");
        }
    }
}
