using System.Diagnostics;
using System.Text.Json;
using System.Windows;

namespace LanCast.Updates;

internal sealed record UpdatePlan(int ParentId, long ParentStarted, string Target, string Sha256, long Size, bool Installed);

internal static class UpdateWorker
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanCast", "Updates");

    internal static async Task LaunchAsync(string directory, UpdateRelease release, CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executável não encontrado.");
        var helper = Path.Combine(directory, "Updater.exe");
        File.Copy(executable, helper);
        using var parent = Process.GetCurrentProcess();
        var plan = new UpdatePlan(parent.Id, parent.StartTime.ToUniversalTime().Ticks, executable, release.Sha256, release.Size, release.Installed);
        var planPath = Path.Combine(directory, "plan.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), cancellationToken);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, WorkingDirectory = directory };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(planPath);
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o atualizador.");
        // Wait for the worker to validate the plan before closing the running app.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        try
        {
            while (!File.Exists(Path.Combine(directory, "ready")))
            {
                if (worker.HasExited || DateTime.UtcNow > deadline) throw new InvalidOperationException("O atualizador não iniciou. O LanCast permanecerá aberto.");
                await Task.Delay(100, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            // A cancelled handoff must never apply later when the user exits normally.
            try { if (!worker.HasExited) worker.Kill(); } catch { }
            throw;
        }
    }

    internal static async Task RunAsync(string planPath)
    {
        UpdatePlan? plan = null;
        bool parentExited = false;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(planPath))!;
            var helper = Path.GetFullPath(Environment.ProcessPath!);
            if (!helper.Equals(Path.Combine(directory, "Updater.exe"), StringComparison.OrdinalIgnoreCase) ||
                !directory.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pasta de atualização inválida.");
            plan = JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(planPath)) ?? throw new InvalidDataException("Atualização inválida.");
            using var parent = Process.GetProcessById(plan.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStarted ||
                !string.Equals(parent.MainModule?.FileName, plan.Target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O processo do LanCast mudou. Tente novamente.");
            var payload = Path.Combine(directory, "payload.exe");
            await UpdateService.VerifyAsync(payload, plan.Sha256, plan.Size, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(directory, "ready"), "ready");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await parent.WaitForExitAsync(timeout.Token);
            parentExited = true;
            if (plan.Installed)
            {
                var setup = new ProcessStartInfo(payload) { UseShellExecute = true, Verb = "runas", WorkingDirectory = directory };
                setup.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /DIR=\"" + Path.GetDirectoryName(plan.Target) + "\"";
                using var installer = Process.Start(setup) ?? throw new IOException("Não foi possível abrir o instalador.");
                await installer.WaitForExitAsync();
                if (installer.ExitCode != 0) throw new IOException($"O instalador terminou com código {installer.ExitCode}. Tente atualizar novamente.");
            }
            else ReplacePortable(payload, plan.Target);
            Restart(plan.Target);
            try { File.Delete(payload); File.Delete(planPath); File.Delete(Path.Combine(directory, "ready")); } catch { }
        }
        catch (Exception ex)
        {
            AppLog.Info("Atualização falhou: " + ex.Message);
            MessageBox.Show("Não foi possível concluir a atualização.\n\n" + ex.Message, "Atualização do LanCast", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (parentExited && plan != null)
                try { Restart(plan.Target); } catch { }
        }
    }

    internal static void ReplacePortable(string payload, string target)
    {
        // Stage on the destination volume so File.Replace can atomically exchange files.
        var staged = target + ".update-" + Guid.NewGuid().ToString("N");
        var backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(payload, staged);
            File.Replace(staged, target, backup);
            try { File.Delete(backup); } catch { }
        }
        catch
        {
            if (File.Exists(backup)) File.Move(backup, target, true);
            throw;
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    private static void Restart(string target) => Process.Start(new ProcessStartInfo(target)
        { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target)! });

    internal static void CleanOldDownloads()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var directory in Directory.GetDirectories(Root))
        {
            // Only our GUID folders, never user-selected paths. Locked workers are skipped.
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || Directory.GetLastWriteTimeUtc(directory) > DateTime.UtcNow.AddDays(-3)) continue;
            try { Directory.Delete(directory, true); } catch { }
        }
    }
}
