using System;
using System.IO;
using System.Text.Json;
using StockInvoiceApp.Models;

namespace StockInvoiceApp.Services;

public sealed class AppEnvironment
{
    public AppSettings Settings { get; }
    public string WorkspaceRoot { get; }
    public string ConnectionString { get; }

    public AppEnvironment()
    {
        WorkspaceRoot = FindWorkspaceRoot();
        Settings = LoadSettings(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        var selected = string.Equals(Settings.AppMode, "prod", StringComparison.OrdinalIgnoreCase)
            ? Settings.ConnectionStrings.ProdDb
            : Settings.ConnectionStrings.DemoDb;

        ConnectionString = NormalizeConnectionString(selected);
    }

    private static AppSettings LoadSettings(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppSettings();
    }

    private static string NormalizeConnectionString(string raw)
    {
        const string prefix = "Data Source=";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        var value = raw[prefix.Length..].Trim();
        if (Path.IsPathRooted(value))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(value) ?? ".");
            return $"Data Source={value}";
        }

        var dbPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, value));
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath) ?? ".");
        return $"Data Source={dbPath}";
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++)
        {
            var candidate = current.FullName;
            var schema = Path.Combine(candidate, "database", "sqlite", "01_schema.sql");
            if (File.Exists(schema))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
