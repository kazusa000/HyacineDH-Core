using System.Diagnostics;
using Spectre.Console;
using System.Collections.Concurrent;

namespace HyacineCore.Server.Util;

public class Logger(string moduleName)
{
    private static FileInfo? _logFile;
    private static FileInfo? _debugLogFile;
    private static readonly object Lock = new();
    private static readonly object HeadlessCallbacks = new();
    private static readonly BlockingCollection<(string Message, LoggerLevel Level)> HeadlessLogs = new(8192);
    private static int DroppedDebugLogs;
    private static readonly Lazy<Thread> HeadlessWriter = new(() =>
    {
        var thread = new Thread(WriteHeadlessLogs) { IsBackground = true, Name = "headless-log-writer" };
        thread.Start();
        return thread;
    });

    private static void WriteHeadlessLogs()
    {
        foreach (var first in HeadlessLogs.GetConsumingEnumerable())
        {
            var batch = new List<(string Message, LoggerLevel Level)> { first };
            while (batch.Count < 256 && HeadlessLogs.TryTake(out var next)) batch.Add(next);
            var dropped = Interlocked.Exchange(ref DroppedDebugLogs, 0);
            if (dropped > 0) batch.Add(($"[{DateTime.Now:HH:mm:ss}] [Logger] [WARN] Dropped {dropped} debug entries while the headless log queue was full.", LoggerLevel.WARN));
            // Redirected services do not need terminal rendering or prompt redraw.
            // Batch file opens and console writes so request threads never wait on them.
            var all = string.Join(Environment.NewLine, batch.Select(x => x.Message));
            try { Console.WriteLine(all); } catch (IOException) { }
            WriteToDebugFile(all);
            var regular = string.Join(Environment.NewLine, batch.Where(x => x.Level != LoggerLevel.DEBUG).Select(x => x.Message));
            if (regular.Length > 0) WriteToFile(regular);
        }
    }

    public static void Flush()
    {
        if (!HeadlessWriter.IsValueCreated) return;
        lock (HeadlessCallbacks) HeadlessLogs.CompleteAdding();
        HeadlessWriter.Value.Join(TimeSpan.FromSeconds(10));
    }

    public void Log(string message, LoggerLevel level)
    {
        if (Console.IsOutputRedirected)
        {
            var prefix = ShouldShowTimeInLog() ? $"[{DateTime.Now:HH:mm:ss}] " : "";
            var line = $"{prefix}[{moduleName}] [{level}] {message}";
            lock (HeadlessCallbacks)
            {
                PluginEventCommon.InvokeOnConsoleLog(line);
                _ = HeadlessWriter.Value;
                if (!HeadlessLogs.IsAddingCompleted)
                {
                    if (level == LoggerLevel.DEBUG)
                    {
                        if (!HeadlessLogs.TryAdd((line, level))) Interlocked.Increment(ref DroppedDebugLogs);
                    }
                    else HeadlessLogs.Add((line, level));
                }
            }
            return;
        }
        lock (Lock)
        {
            var now = DateTime.Now;
            var showTime = ShouldShowTimeInLog();
            var timeText = now.ToString("HH:mm:ss");
            var savedInput = IConsole.Input.ToList(); // Copy
            IConsole.RedrawInput("", false);
            var timeMarkup = showTime ? $"[[[bold deepskyblue3_1]{timeText}[/]]] " : string.Empty;
            AnsiConsole.Write(new Markup($"{timeMarkup}" +
                                         $"[[[gray]{moduleName}[/]]] [[[{(ConsoleColor)level}]{level}[/]]] {message.Replace("[", "[[").Replace("]", "]]")}\n"));

            IConsole.RedrawInput(savedInput);

            var logMessage = showTime
                ? $"[{timeText}] [{moduleName}] [{level}] {message}"
                : $"[{moduleName}] [{level}] {message}";
            PluginEventCommon.InvokeOnConsoleLog(logMessage);

            if (level == LoggerLevel.DEBUG)
            {
                WriteToDebugFile(logMessage);
                return;
            }

            WriteToDebugFile(logMessage);
            WriteToFile(logMessage);
        }
    }

    public void Info(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.INFO);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.INFO);
            Log(e.StackTrace!, LoggerLevel.INFO);
        }
    }

    public void Warn(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.WARN);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.WARN);
            Log(e.StackTrace!, LoggerLevel.WARN);
        }
    }

    public void Error(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.ERROR);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.ERROR);
            Log(e.StackTrace!, LoggerLevel.ERROR);
        }
    }

    public void Fatal(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.FATAL);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.FATAL);
            Log(e.StackTrace!, LoggerLevel.FATAL);
        }
    }

    public void Debug(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.DEBUG);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.DEBUG);
            Log(e.StackTrace!, LoggerLevel.DEBUG);
        }
    }

    public static void SetLogFile(FileInfo file)
    {
        _logFile = file;
    }

    public static void SetDebugLogFile(FileInfo file)
    {
        _debugLogFile = file;
    }

    public static void WriteToFile(string message)
    {
        try
        {
            if (_logFile == null) throw new Exception("LogFile is not set");
            using var sw = _logFile.AppendText();
            sw.WriteLine(message);
        }
        catch
        {
        }
    }

    public static void WriteToDebugFile(string message)
    {
        try
        {
            if (_debugLogFile == null) throw new Exception("DebugLogFile is not set");
            using var sw = _debugLogFile.AppendText();
            sw.WriteLine(message);
        }
        catch
        {
        }
    }

    public static Logger GetByClassName()
    {
        return new Logger(new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "");
    }

    public static bool ShouldShowTimeInLog()
    {
        try
        {
            return ConfigManager.Config.ServerOption.LogOption.ShowTimeInLog;
        }
        catch
        {
            return true;
        }
    }
}

public enum LoggerLevel
{
    INFO = ConsoleColor.Cyan,
    WARN = ConsoleColor.Yellow,
    ERROR = ConsoleColor.Red,
    FATAL = ConsoleColor.DarkRed,
    DEBUG = ConsoleColor.Blue
}
