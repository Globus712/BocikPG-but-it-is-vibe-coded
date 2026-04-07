using System;
using DSharpPlus.Commands.ContextChecks;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireOwnerAttribute : ContextCheckAttribute
{
    // No extra logic needed – the check class will handle it
}