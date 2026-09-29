using System;
using System.Collections.Generic;
using SaveState;

namespace SaveState.Tests;

/// <summary>Collects log entries into a list so that tests can assert "the failure was recorded, not swallowed".</summary>
internal sealed class CapturingLogger : ISaveLogger
{
    public List<string> Entries { get; } = new List<string>();

    public bool Has(SaveLogLevel level, string fragment)
    {
        foreach (string entry in Entries)
        {
            if (entry.StartsWith(level.ToString(), StringComparison.Ordinal)
                && entry.IndexOf(fragment, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public void Log(SaveLogLevel level, string message, Exception? exception = null)
    {
        Entries.Add(level + ": " + message + (exception == null ? string.Empty : " | " + exception.Message));
    }
}

/// <summary>A store whose writes always blow up: verifies that "a failed write only logs and never throws the exception into the business layer".</summary>
internal sealed class ThrowingStore : ISaveStore
{
    /// <summary>Lets a test case choose between the Missing branch and a failing Read.</summary>
    public bool ExistsResult { get; set; } = true;

    public bool Exists(string fileName)
    {
        return ExistsResult;
    }

    public string Read(string fileName)
    {
        throw new System.IO.IOException("read failure (injected by the test)");
    }

    public void Write(string fileName, string contents)
    {
        throw new System.IO.IOException("write failure (injected by the test)");
    }

    public bool Delete(string fileName)
    {
        return false;
    }

    public string Describe(string fileName)
    {
        return "throwing://" + fileName;
    }
}
