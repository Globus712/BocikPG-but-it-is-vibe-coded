using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Trees;

/// <summary>
/// Represents a single command definition read from the JSON file.
/// </summary>
public class CommandDefinition
{
    public string name { get; set; } = "";
    public string description { get; set; } = "";
    public string response { get; set; } = "";
}

/// <summary>
/// Factory for building dynamic commands from a JSON configuration file.
/// </summary>
public static class CommandBuilderFactory
{
    /// <summary>
    /// Creates a <see cref="CommandBuilder"/> from a name, description, and response string.
    /// Uses an explicit delegate type to avoid reflection issues with lambdas.
    /// </summary>
    private static CommandBuilder CreateCommandBuilder(string name, string description, string response)
    {
        // Explicit delegate type prevents the compiler from generating a closure class
        // that confuses DSharpPlus.Commands' invocation emitter.
        Func<CommandContext, Task> executeMethod = async (CommandContext ctx) =>
        {
            await ctx.RespondAsync(response);
        };

        return new CommandBuilder()
            .WithName(name)
            .WithDescription(description)
            .WithDelegate(executeMethod);
    }

    /// <summary>
    /// Reads a JSON file and deserializes it into a list of <see cref="CommandDefinition"/>.
    /// </summary>
    /// <param name="filePath">Path to the JSON file.</param>
    /// <returns>List of command definitions; empty list if the file is missing or invalid.</returns>
    public static List<CommandDefinition> LoadCommandDefinitionsFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new List<CommandDefinition>();

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<CommandDefinition>>(json)
                   ?? new List<CommandDefinition>();
        }
        catch (Exception ex)
        {
            // Logging would be ideal here, but we keep it silent to avoid breaking command registration.
            return new List<CommandDefinition>();
        }
    }

    /// <summary>
    /// Builds a list of <see cref="CommandBuilder"/> objects from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the JSON file.</param>
    /// <returns>List of command builders ready to be added to the command processor.</returns>
    public static List<CommandBuilder> BuildCommandsFromJsonFile(string filePath)
    {
        var definitions = LoadCommandDefinitionsFromFile(filePath);
        var builders = new List<CommandBuilder>(definitions.Count);

        foreach (var def in definitions)
        {
            // Skip invalid entries
            if (string.IsNullOrWhiteSpace(def.name) || string.IsNullOrWhiteSpace(def.response))
                continue;

            builders.Add(CreateCommandBuilder(def.name, def.description, def.response));
        }

        return builders;
    }
}