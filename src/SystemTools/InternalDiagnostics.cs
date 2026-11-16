namespace SystemTools;

internal static class InternalDiagnostics
{
    public static bool Enabled =>
        IsSet("SystemTools_InternalDiagnostics") ||
        IsSet("SystemTools_M1_SmokeTest") ||
        IsSet("SystemTools_M2_Acceptance") ||
        IsSet("SystemTools_M4_Acceptance") ||
        IsSet("SystemTools_M5_Acceptance") ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SystemTools_M3_ProbeRoot"));

    private static bool IsSet(string name) => Environment.GetEnvironmentVariable(name) == "1";
}
