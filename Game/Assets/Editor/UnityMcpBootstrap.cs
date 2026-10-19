using UnityEditor;

namespace WildernessExtraction.Editor
{
    /// <summary>
    /// Keeps the shared Unity MCP bridge ready for Codex and Claude Code.
    /// These preferences are machine-local and do not affect player builds.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityMcpBootstrap
    {
        private const string ReloadRequestedKey = "WildernessExtraction.UnityMcpBootstrap.ReloadRequested";

        static UnityMcpBootstrap()
        {
            var changed = false;
            changed |= EnsureBool("MCPForUnity.UseHttpTransport", true);
            changed |= EnsureString("MCPForUnity.HttpTransportScope", "local");
            changed |= EnsureString("MCPForUnity.HttpUrl", "http://127.0.0.1:8080");
            changed |= EnsureBool("MCPForUnity.ProjectScopedTools.LocalHttp", true);
            changed |= EnsureBool("MCPForUnity.AutoStartOnLoad", true);
            changed |= EnsureBool("MCPForUnity.SetupCompleted", true);

            // MCP's auto-start handler reads its preference during domain load.
            // Request one additional reload only when this script changed a value.
            if (changed && !SessionState.GetBool(ReloadRequestedKey, false))
            {
                SessionState.SetBool(ReloadRequestedKey, true);
                EditorApplication.delayCall += EditorUtility.RequestScriptReload;
            }
        }

        private static bool EnsureBool(string key, bool value)
        {
            if (EditorPrefs.GetBool(key, !value) == value)
            {
                return false;
            }

            EditorPrefs.SetBool(key, value);
            return true;
        }

        private static bool EnsureString(string key, string value)
        {
            if (EditorPrefs.GetString(key, string.Empty) == value)
            {
                return false;
            }

            EditorPrefs.SetString(key, value);
            return true;
        }
    }
}
