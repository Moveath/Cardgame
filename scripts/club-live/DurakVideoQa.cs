// Compatibility entry point for existing Durak QA commands.
// Copy BOTH this file and ClubTablesVideoQa.cs into the isolated project's Assets/Scripts/Editor.
// Run with graphics enabled: -executeMethod DurakVideoQa.Run (no -quit / -nographics).
// DURAK_VIDEO_QA_OUTPUT retains its original meaning; the shared harness checks real HasVariation.
using System;

public static class DurakVideoQa
{
    public static void Run()
    {
        var output = Environment.GetEnvironmentVariable("DURAK_VIDEO_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("Set DURAK_VIDEO_QA_OUTPUT first.");
        Environment.SetEnvironmentVariable("CLUB_TABLES_QA_OUTPUT", output);
        Environment.SetEnvironmentVariable("CLUB_TABLES_QA_GAMES", "durak");
        ClubTablesVideoQa.Run();
    }
}
