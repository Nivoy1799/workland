using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027 AC3: the tour code is compiled out of release builds. Scripts/Dev is its own assembly with the define
    /// constraint "UNITY_EDITOR || DEVELOPMENT_BUILD" (Unity skips the whole assembly when neither is defined), no
    /// release assembly references it, and the few hooks in Worktown.Game sit inside #if DEVELOPMENT_BUILD || UNITY_EDITOR.
    /// </summary>
    public class DevBuildTests
    {
        private const string DevGuard = "DEVELOPMENT_BUILD || UNITY_EDITOR";
        private static readonly string[] HookTokens = { "IIntentSource", "IntentSource", "ScriptedIntentSource", "Worktown.Game.Dev", "DevClick", "DevSend" };

        private static string ProjectFolder => Path.Combine(Application.dataPath, "_Project");
        private static string DevAsmdef => Path.Combine(ProjectFolder, "Scripts", "Dev", "Worktown.Game.Dev.asmdef");

        [Serializable]
        private class AsmdefFile
        {
            public string name;
            public string[] references;
            public string[] includePlatforms;
            public string[] defineConstraints;
            public bool autoReferenced;
        }

        private static AsmdefFile Load(string path) => JsonUtility.FromJson<AsmdefFile>(File.ReadAllText(path));

        [Test]
        public void DevAssembly_HasTheDevelopmentConstraint()
        {
            Assert.IsTrue(File.Exists(DevAsmdef), DevAsmdef);
            AsmdefFile asmdef = Load(DevAsmdef);
            Assert.AreEqual("Worktown.Game.Dev", asmdef.name);
            CollectionAssert.AreEqual(new[] { "UNITY_EDITOR || DEVELOPMENT_BUILD" }, asmdef.defineConstraints);
            Assert.IsFalse(asmdef.autoReferenced, "nothing picks it up by accident");
        }

        [Test]
        public void DevConstraint_ExcludesReleaseBuilds()
        {
            string[] constraint = Load(DevAsmdef).defineConstraints;
            // A release Windows player and a release Linux server: no UNITY_EDITOR, no DEVELOPMENT_BUILD.
            Assert.IsFalse(Compatible(new[] { "UNITY_STANDALONE", "UNITY_STANDALONE_WIN", "ENABLE_MONO" }, constraint), "release client");
            Assert.IsFalse(Compatible(new[] { "UNITY_STANDALONE", "UNITY_STANDALONE_LINUX", "UNITY_SERVER" }, constraint), "release server");
            Assert.IsTrue(Compatible(new[] { "UNITY_STANDALONE_WIN", "DEVELOPMENT_BUILD" }, constraint), "development client");
            Assert.IsTrue(Compatible(new[] { "UNITY_EDITOR", "UNITY_EDITOR_WIN" }, constraint), "editor");
        }

        [Test]
        public void NoReleaseAssembly_ReferencesTheDevAssembly()
        {
            foreach (string path in Directory.GetFiles(ProjectFolder, "*.asmdef", SearchOption.AllDirectories))
            {
                AsmdefFile asmdef = Load(path);
                bool editorOnly = asmdef.includePlatforms != null && asmdef.includePlatforms.SequenceEqual(new[] { "Editor" });
                bool constrained = asmdef.defineConstraints != null && asmdef.defineConstraints.Length > 0;
                if (asmdef.name == "Worktown.Game.Dev" || editorOnly || constrained) continue;
                CollectionAssert.DoesNotContain(asmdef.references ?? new string[0], "Worktown.Game.Dev", path);
            }
        }

        [Test]
        public void GameHooks_AreInsideTheDevGuard()
        {
            int hooks = 0;
            string scripts = Path.Combine(ProjectFolder, "Scripts");
            string dev = Path.Combine(scripts, "Dev");
            foreach (string path in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                if (path.StartsWith(dev, StringComparison.OrdinalIgnoreCase)) continue;
                var guards = new Stack<bool>();
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("#if ")) { guards.Push(line.Substring(4).Trim() == DevGuard); continue; }
                    if (line.StartsWith("#elif") || line.StartsWith("#else")) { if (guards.Count > 0) { guards.Pop(); guards.Push(false); } continue; }
                    if (line.StartsWith("#endif")) { if (guards.Count > 0) guards.Pop(); continue; }
                    if (!HookTokens.Any(t => line.Contains(t))) continue;
                    hooks++;
                    Assert.IsTrue(guards.Contains(true), Path.GetFileName(path) + ":" + (i + 1) + " uses a dev hook outside #if " + DevGuard + ": " + line);
                }
            }
            Assert.Greater(hooks, 0, "the PlayerController and PickupInteractor hooks were found");
        }

        /// <summary>Unity's own define-constraint check (internal), or the same rule for the "A || B" form if it moved.</summary>
        private static bool Compatible(string[] defines, string[] constraints)
        {
            Type helper = typeof(UnityEditor.Editor).Assembly.GetTypes().FirstOrDefault(t => t.Name == "DefineConstraintsHelper");
            MethodInfo method = helper?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "IsDefineConstraintsCompatible" && m.ReturnType == typeof(bool)
                    && m.GetParameters().Length == 2 && m.GetParameters().All(p => p.ParameterType == typeof(string[])));
            if (method != null) return (bool)method.Invoke(null, new object[] { defines, constraints });
            return constraints.All(c => c.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Any(term => term.StartsWith("!") ? !defines.Contains(term.Substring(1)) : defines.Contains(term)));
        }
    }
}
