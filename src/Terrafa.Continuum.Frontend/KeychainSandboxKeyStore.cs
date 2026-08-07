// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Diagnostics;
using System.Text;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend;

/// <summary>
/// The desktop head's home for the operator's Anthropic API key: the macOS keychain, through the
/// same <c>security</c> CLI as <see cref="KeychainSecretStore"/>, under its own account name so
/// the two credentials live and die independently. On any other OS every call quietly does
/// nothing and the key lasts as long as the process.
/// </summary>
internal sealed class KeychainSandboxKeyStore : ISandboxKeyStore
{
    private const string Service = "com.terrafa.continuum";
    private const string Account = "anthropic";

    public async Task<string?> LoadAsync()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var (exitCode, output) = await RunSecurityAsync(
            "find-generic-password", "-s", Service, "-a", Account, "-w");
        if (exitCode != 0) return null;
        return Decode(output.TrimEnd('\r', '\n'));
    }

    public async Task SaveAsync(string apiKey)
    {
        if (!OperatingSystem.IsMacOS()) return;
        // Base64 for the same reason the auth store does it: `find-generic-password -w` prints
        // any value with a non-printable byte as bare hex, which would never round-trip.
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(apiKey));
        await RunSecurityAsync(
            "add-generic-password", "-U", "-s", Service, "-a", Account, "-w", encoded);
    }

    public async Task ClearAsync()
    {
        if (!OperatingSystem.IsMacOS()) return;
        await RunSecurityAsync("delete-generic-password", "-s", Service, "-a", Account);
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
