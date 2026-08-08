// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Diagnostics;
using System.Text;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend;

/// <summary>
/// Reads and writes the same keychain entry <c>KeychainSecretStore</c> (the desktop head) does —
/// same service, same account — so this process shares whatever session is already signed in over
/// there. This process is never where a sign-in happens, but it still has to write back a rotated
/// refresh token if Cognito issues one on renewal: leaving that on the floor would let this
/// process's copy and the desktop app's copy drift, and whichever restores second would restore
/// against an already-superseded token.
///
/// <para>
/// macOS only, like the entry it reads. On Windows the desktop head has nowhere durable to put a
/// session either, so there is nothing for a standalone process to find — <see cref="LoadAsync"/>
/// answers null there and the tool reports "not signed in" rather than failing to start.
/// </para>
/// </summary>
internal sealed class KeychainAuthStore : ISecretStore
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
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.ToStorageValue()));
        await RunSecurityAsync(
            "add-generic-password", "-U", "-s", Service, "-a", Account, "-w", encoded);
    }

    // Deliberately a no-op: a background renewal failing here (offline, a transient outage) is not
    // grounds to sign the operator out of the desktop app they can actually see and react to.
    public Task ClearAsync() => Task.CompletedTask;

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
