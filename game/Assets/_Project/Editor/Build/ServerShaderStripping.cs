using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Editor.Build
{
    /// <summary>
    /// Second review of spec 027d: the headless dedicated server never draws, so a server build compiles no variant of the
    /// client-only world text shader (<see cref="LabelMaterials.IsClientOnlyShader"/>). Client builds and URP rendering are
    /// unchanged. The build's subtarget is read when the build starts and forgotten when it ends.
    /// </summary>
    public sealed class ServerShaderStripping : IPreprocessBuildWithReport, IPostprocessBuildWithReport, IPreprocessShaders
    {
        private static bool _serverBuild;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            _serverBuild = report.summary.GetSubtarget<StandaloneBuildSubtarget>() == StandaloneBuildSubtarget.Server;
            if (_serverBuild) Debug.Log("[Build] server build: client-only shaders are stripped");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            _serverBuild = false;
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (_serverBuild && shader != null && LabelMaterials.IsClientOnlyShader(shader.name)) data.Clear();
        }
    }
}
