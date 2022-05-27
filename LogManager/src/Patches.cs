using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NLog;

namespace LogManager
{
    internal static class Patches
    {
        internal const string HarmonyID = "com.flsoz.ttmodding.logmanager";
        internal static Harmony harmony = new Harmony(HarmonyID);
        internal static Logger logger;

        internal static void Init()
        {
            harmony.PatchAll();

            LogManager.TargetConfig targetConfig = new LogManager.TargetConfig {
                layout = "${longdate} | ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${message}  ${exception}"
            };

            // create a logger that writes to /Logs/output.log
            LogTarget target = TTLogManager.RegisterLoggingTarget("output", targetConfig);
            logger = NLog.LogManager.GetLogger("vanilla");
            LogLevel minLevel = TTLogManager.ConfiguredGlobalLogLevel != null ? TTLogManager.ConfiguredGlobalLogLevel : LogLevel.Info;
            TTLogManager.RegisterLogger(logger, target, minLevel);
        }

        internal static string FormatMessage(string message, string intermediary = "")
        {
            if (message.Length > 0 && message[0] == '[')
            {
                int index = message.IndexOf(']');
                if (index > 0)
                {
                    return $"{message.Substring(1, index - 1)} |{message.Substring(index + 1)}";
                }
            }
            return message;
        }

        internal static void FormatMessageWithCallstack(ref string message)
        {
            if (d.EnableLogCallstacks)
            {
                string stackTrace = d.GetStackTrace(2);
                message = FormatMessage(message) + "\n" + stackTrace;
            }
        }

        [HarmonyPatch(typeof(d), "Log", new Type[] { typeof(object), typeof(UnityEngine.Object) })]
        internal static class PatchLogContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(object message)
            {
                if (message is string)
                {
                    logger.Info(FormatMessage(message as string));
                }
                else
                {
                    logger.Info(message);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "Log", new Type[] { typeof(object) })]
        internal static class PatchLog
        {
            [HarmonyPrefix]
            internal static bool Prefix(object message)
            {
                if (message is string)
                {
                    logger.Info(FormatMessage(message as string));
                }
                else
                {
                    logger.Info(message);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogWarning", new Type[] { typeof(object) })]
        internal static class PatchWarning
        {
            [HarmonyPrefix]
            internal static bool Prefix(object message)
            {
                if (message is string)
                {
                    logger.Warn(FormatMessage(message as string));
                }
                else
                {
                    logger.Warn(message);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogWarning", new Type[] { typeof(object), typeof(UnityEngine.Object) })]
        internal static class PatchWarningContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(object message)
            {
                if (message is string)
                {
                    logger.Warn(FormatMessage(message as string));
                }
                else
                {
                    logger.Warn(message);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogError", new Type[] { typeof(object), typeof(UnityEngine.Object) })]
        internal static class PatchErrorContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(object message)
            {
                if (message is string)
                {
                    string messageBody = FormatMessage(message as string);
                    FormatMessageWithCallstack(ref messageBody);
                    logger.Error(messageBody);
                }
                else
                {
                    string callstack = "";
                    FormatMessageWithCallstack(ref callstack);
                    logger.Error("{}{}", message, callstack);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogError", new Type[] { typeof(object) })]
        internal static class PatchError
        {
            [HarmonyPrefix]
            internal static bool Prefix(object message)
            {
                if (message is string)
                {
                    string messageBody = FormatMessage(message as string);
                    FormatMessageWithCallstack(ref messageBody);
                    logger.Error(messageBody);
                }
                else
                {
                    string callstack = "";
                    FormatMessageWithCallstack(ref callstack);
                    logger.Error("{}{}", message, callstack);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogException", new Type[] { typeof(Exception), typeof(UnityEngine.Object) })]
        internal static class PatchExceptionContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(Exception exception)
            {
                logger.Fatal(exception);
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogException", new Type[] { typeof(Exception) })]
        internal static class PatchException
        {
            [HarmonyPrefix]
            internal static bool Prefix(Exception exception)
            {
                logger.Fatal(exception);
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogFormat", new Type[] { typeof(UnityEngine.Object), typeof(string), typeof(object[]) })]
        internal static class PatchFormatContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(string format, params object[] args)
            {
                logger.Info(FormatMessage(format), args);
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogFormat", new Type[] { typeof(string), typeof(object[]) })]
        internal static class PatchFormat
        {
            [HarmonyPrefix]
            internal static bool Prefix(string format, params object[] args)
            {
                logger.Info(FormatMessage(format), args);
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogWarningFormat", new Type[] { typeof(string), typeof(object[]) })]
        internal static class PatchFormatWarning
        {
            [HarmonyPrefix]
            internal static bool Prefix(string format, params object[] args)
            {
                logger.Warn(FormatMessage(format), args);
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogWarningFormat", new Type[] { typeof(UnityEngine.Object), typeof(string), typeof(object[]) })]
        internal static class PatchFormatWarningContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(string format, params object[] args)
            {
                logger.Warn(FormatMessage(format), args);
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogErrorFormat", new Type[] { typeof(string), typeof(object[]) })]
        internal static class PatchFormatError
        {
            [HarmonyPrefix]
            internal static bool Prefix(string format, params object[] args)
            {
                string message = FormatMessage(String.Format(format, args));
                FormatMessageWithCallstack(ref message);
                logger.Error(message);
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogErrorFormat", new Type[] { typeof(UnityEngine.Object), typeof(string), typeof(object[]) })]
        internal static class PatchFormatErrorContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(string format, params object[] args)
            {
                string message = FormatMessage(String.Format(format, args));
                FormatMessageWithCallstack(ref message);
                logger.Error(message);
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogAssertion", new Type[] { typeof(object) })]
        internal static class PatchAssertion
        {
            [HarmonyPrefix]
            internal static bool Prefix(object message)
            {
                if (message is string)
                {
                    string messageBody = FormatMessage(message as string);
                    FormatMessageWithCallstack(ref messageBody);
                    logger.Debug("ASSERT | {}", messageBody);
                }
                else
                {
                    string callstack = "";
                    FormatMessageWithCallstack(ref callstack);
                    logger.Debug("ASSERT | {}{}", message, callstack);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "LogAssertion", new Type[] { typeof(object), typeof(UnityEngine.Object) })]
        internal static class PatchAssertionContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(object message)
            {
                if (message is string)
                {
                    string messageBody = FormatMessage(message as string);
                    FormatMessageWithCallstack(ref messageBody);
                    logger.Debug("ASSERT | {}", messageBody);
                }
                else
                {
                    string callstack = "";
                    FormatMessageWithCallstack(ref callstack);
                    logger.Debug("ASSERT | {}{}", message, callstack);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "Assert", new Type[] { typeof(bool) })]
        internal static class PatchAssert
        {
            [HarmonyPrefix]
            internal static bool Prefix(bool condition)
            {
                if (!condition)
                {
                    string message = "ASSERT FAILED | ";
                    FormatMessageWithCallstack(ref message);
                    logger.Error(message);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "Assert", new Type[] { typeof(bool), typeof(string) })]
        internal static class PatchAssertMessage
        {
            [HarmonyPrefix]
            internal static bool Prefix(bool condition, string errorMessage)
            {
                if (!condition)
                {
                    string callstack = "";
                    FormatMessageWithCallstack(ref callstack);
                    logger.Error("{}{}", FormatMessage(errorMessage, "ASSERT FAILED"), callstack);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "Assert", new Type[] { typeof(bool), typeof(string), typeof(UnityEngine.Object) })]
        internal static class PatchAssertMessageContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(bool condition, string errorMessage)
            {
                if (!condition)
                {
                    string callstack = "";
                    FormatMessageWithCallstack(ref callstack);
                    logger.Error("{}{}", FormatMessage(errorMessage, "ASSERT FAILED"), callstack);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "AssertFormat", new Type[] { typeof(bool), typeof(string), typeof(object[]) })]
        internal static class PatchAssertFormatMessage
        {
            [HarmonyPrefix]
            internal static bool Prefix(bool condition, string format, params object[] args)
            {
                if (!condition)
                {
                    string callstack = "";
                    FormatMessageWithCallstack(ref callstack);
                    logger.Error("{}{}", FormatMessage(String.Format(format, args), "ASSERT FAILED"), callstack);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }

        [HarmonyPatch(typeof(d), "AssertFormat", new Type[] { typeof(bool), typeof(UnityEngine.Object), typeof(string), typeof(object[]) })]
        internal static class PatchAssertFormatMessageContext
        {
            [HarmonyPrefix]
            internal static bool Prefix(bool condition, string format, params object[] args)
            {
                if (!condition)
                {
                    string callstack = "";
                    FormatMessageWithCallstack(ref callstack);
                    logger.Error("{}{}", FormatMessage(String.Format(format, args), "ASSERT FAILED"), callstack);
                }
                return TTLogManager.EnableVanillaLogs;
            }
        }
    }
}
