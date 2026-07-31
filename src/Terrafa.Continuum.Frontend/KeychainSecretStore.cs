// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Diagnostics;
using System.Text;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend;

/// <summary>
/// The desktop head's credential store: the macOS keychain, driven through the <c>security</c>
/// CLI. Three invocations replace what a NuGet dependency would otherwise be pulled in for, and
/// the entry gets the keychain's at-rest protection rather than a file in the profile. On any
/// other OS every call quietly does nothing, which leaves the app exactly as it was before
/// durable sign-in existed.
/// </summary>
internal sealed class KeychainSecretStore : ISecretStore
{
    private const string Service = "com.terrafa.continuum";
    private const string Account = "auth";

    public async Task<StoredCredential?> LoadAsync()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var (exitCode, output) = await RunSecurityAsync(
            "find-generic-password", "-s", Service, "-a", Account, "-w");
        if (exitCode != 0) return null;
        return StoredCredential.FromStorageValue(Decode(output.TrimEnd('\r', '\n')));
    }

    public async Task SaveAsync(StoredCredential credential)
    {
        if (!OperatingSystem.IsMacOS()) return;
        // Base64, because `find-generic-password -w` prints a value containing any non-printable
        // byte — such as this format's newline separator — as bare hex, which would never parse.
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.ToStorageValue()));
        await RunSecurityAsync(
            "add-generic-password", "-U", "-s", Service, "-a", Account, "-w", encoded);
    }

    private static string? Decode(string encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async Task ClearAsync()
    {
        if (!OperatingSystem.IsMacOS()) return;
        await RunSecurityAsync("delete-generic-password", "-s", Service, "-a", Account);
    }

    private static async Task<(int ExitCode, string Output)> RunSecurityAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return (-1, string.Empty);
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, output);
        }
        catch (Exception)
        {
            return (-1, string.Empty);
        }
    }
}
