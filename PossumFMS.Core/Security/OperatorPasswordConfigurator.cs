using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;

namespace PossumFMS.Core.Security;

public static class OperatorPasswordConfigurator
{
    public static int Configure(string configurationPath)
    {
        Console.Write("New operator password: ");
        var password = ReadPassword();
        Console.Write("Confirm operator password: ");
        var confirmation = ReadPassword();

        if (string.IsNullOrWhiteSpace(password) || password != confirmation)
        {
            Console.Error.WriteLine("Passwords must be non-empty and match.");
            return 1;
        }

        var root = JsonNode.Parse(File.ReadAllText(configurationPath))?.AsObject()
            ?? throw new InvalidOperationException("appsettings.json must contain a JSON object.");
        var security = root["Security"]?.AsObject() ?? new JsonObject();
        root["Security"] = security;
        security["OperatorPasswordHash"] = new PasswordHasher<object>().HashPassword(new object(), password);

        var temporaryPath = configurationPath + ".tmp";
        File.WriteAllText(temporaryPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, configurationPath, overwrite: true);
        Console.WriteLine("Operator password updated.");
        return 0;
    }

    private static string ReadPassword()
    {
        var value = new System.Text.StringBuilder();
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && value.Length > 0)
                value.Length--;
            else if (!char.IsControl(key.KeyChar))
                value.Append(key.KeyChar);
        }
        Console.WriteLine();
        return value.ToString();
    }
}