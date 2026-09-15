using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MyIme.Shared
{
    public enum LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }

    /// <summary>
    /// 简单日志：写 %APPDATA%\MyIME\logs\myime-yyyyMMdd.log，同时输出到 DebugView。
    /// 输入法出问题时，用户把日志发过来就能定位。
    /// </summary>
    public static class Log
    {
        static readonly object Gate = new object();
        static string _path;
        static bool _console;

        public static LogLevel MinLevel = LogLevel.Info;

        public static bool ConsoleEcho
        {
            get { return _console; }
            set { _console = value; }
        }

        public static string CurrentPath
        {
            get
            {
                lock (Gate)
                {
                    EnsurePath();
                    return _path;
                }
            }
        }

        static StreamWriter _writer;

        static void EnsurePath()
        {
            if (_path != null && _writer != null) return;
            try
            {
                string dir = Path.Combine(AppPaths.DataDir, "logs");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "myime-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }
            catch
            {
                _path = Path.Combine(Path.GetTempPath(), "myime.log");
            }
        }

        /// <summary>
        /// 保持一个常开的 StreamWriter。
        /// 每次写日志都 File.AppendAllText（开关文件）在键盘钩子里是不可接受的慢，
        /// 会把钩子回调拖到 Windows 的 LowLevelHooksTimeout 之外，导致钩子被系统摘掉。
        /// </summary>
        static void EnsureWriter()
        {
            EnsurePath();
            if (_writer != null) return;
            try
            {
                FileStream fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fs, new UTF8Encoding(false));
                _writer.AutoFlush = true;
            }
            catch
            {
                try
                {
                    string alt = Path.Combine(Path.GetTempPath(), "myime.log");
                    FileStream fs2 = new FileStream(alt, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    _writer = new StreamWriter(fs2, new UTF8Encoding(false));
                    _writer.AutoFlush = true;
                    _path = alt;
                }
                catch { _writer = null; }
            }
        }

        public static void Debug(string msg) { Write(LogLevel.Debug, msg); }
        public static void Info(string msg) { Write(LogLevel.Info, msg); }
        public static void Warn(string msg) { Write(LogLevel.Warn, msg); }

        public static void Error(string msg) { Write(LogLevel.Error, msg); }

        public static void Error(string msg, Exception ex)
        {
            Write(LogLevel.Error, msg + " :: " + (ex == null ? "(null)" : ex.GetType().Name + ": " + ex.Message));
            // 堆栈一定要按 Error 级别写：用户发过来的日志里没有堆栈就等于没线索。
            Exception cur = ex;
            int depth = 0;
            while (cur != null && depth < 5)
            {
                if (cur.StackTrace != null) Write(LogLevel.Error, "  堆栈: " + cur.StackTrace.Replace(Environment.NewLine, " | "));
                cur = cur.InnerException;
                if (cur != null) Write(LogLevel.Error, "  内层异常: " + cur.GetType().Name + ": " + cur.Message);
                depth++;
            }
        }

        public static void Write(LogLevel level, string msg)
        {
            if (level < MinLevel) return;
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level.ToString().ToUpperInvariant() + "] " + msg;
            try { Debugger.Log(0, "MyIME", line + Environment.NewLine); }
            catch { }
            if (_console)
            {
                try { Console.WriteLine(line); } catch { }
            }
            try
            {
                lock (Gate)
                {
                    EnsureWriter();
                    if (_writer != null)
                    {
                        _writer.WriteLine(line);
                        _writtenLines++;
                        if ((_writtenLines & 0x3FF) == 0) TrimIfHuge();
                    }
                }
            }
            catch { }
        }

        static int _writtenLines;

        static void TrimIfHuge()
        {
            try
            {
                if (_writer != null && _path == null) return;
                FileInfo fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                {
                    if (_writer != null) { _writer.Flush(); _writer.Dispose(); _writer = null; }
                    string bak = _path + ".1";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(_path, bak);
                }
            }
            catch { }
        }

        /// <summary>退出前把缓冲刷干净。</summary>
        public static void Flush()
        {
            lock (Gate)
            {
                try { if (_writer != null) _writer.Flush(); } catch { }
            }
        }
    }
}
