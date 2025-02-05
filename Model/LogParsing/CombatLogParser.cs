﻿using SWTORCombatParser.DataStructures;
using SWTORCombatParser.Model.CombatParsing;
using SWTORCombatParser.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SWTORCombatParser.Model.LogParsing
{

    public static class CombatLogParser
    {
        private static Encoding _fileEncoding;

        public static void SetParseDate()
        {
            _7_0LogParsing.SetStartDate();
            _fileEncoding = Encoding.GetEncoding(1252);
        }
        public static ParsedLogEntry ParseLine(string logEntry, long lineIndex, DateTime previousLogTime, bool realTime = true)
        {
            try
            {
                var listEntries = GetInfoComponents(logEntry);

                return _7_0LogParsing.ParseLog(logEntry, previousLogTime, lineIndex, listEntries, realTime);

            }
            catch (Exception e)
            {
                Logging.LogError("Log parsing error: " + e.Message + "\r\n" + logEntry);
                return new ParsedLogEntry() { LogBytes = _fileEncoding.GetByteCount(logEntry), Error = ErrorType.IncompleteLine };
            }
        }
        private static bool GetAllLines(StreamReader sr, List<string> lines)
        {
            bool hasValidEnd = false;
            StringBuilder newLine = new StringBuilder();
            bool lastValueWasbsR = false;
            while (!sr.EndOfStream)
            {
                char[] readChars = new char[2500];
                sr.Read(readChars, 0, 2500);

                for (var c = 0; c < readChars.Length; c++)
                {
                    if (readChars[c] == '\0')
                    {
                        lastValueWasbsR = false;
                        break;
                    }
                    if (readChars[c] == '\r')
                    {
                        lastValueWasbsR = true;
                        continue;
                    }
                    if (readChars[c] == '\n' && lastValueWasbsR)
                    {
                        lastValueWasbsR = false;
                        if (readChars[2499] == '\0' || sr.EndOfStream)
                        {
                            if (c == readChars.Length - 1 || readChars[c + 1] == '\0')
                            {
                                lines.Add(newLine.ToString() + Environment.NewLine);
                                break;
                            }
                            else
                            {
                                if (newLine.Length == 0)
                                    continue;
                                lines.Add(newLine.ToString() + Environment.NewLine);
                                newLine.Clear();
                            }
                        }
                        if (newLine.Length == 0)
                            continue;
                        lines.Add(newLine.ToString() + Environment.NewLine);
                        newLine.Clear();

                    }
                    else
                    {
                        newLine.Append(readChars[c]);
                        lastValueWasbsR = false;
                    }
                }
            }

            return hasValidEnd;
        }
        public static List<ParsedLogEntry> ParseAllLines(CombatLogFile combatLog, bool includeIncomplete = false)
        {
            CombatLogStateBuilder.ClearState();

            var logLines = new List<string>();
            var worked = GetAllLines(combatLog.Data, logLines);

            var numberOfLines = logLines.Count;
            ParsedLogEntry[] parsedLog = new ParsedLogEntry[numberOfLines];
            List<ParsedLogEntry> incompleteLines = new List<ParsedLogEntry>();
            Parallel.For(0, numberOfLines, new ParallelOptions { MaxDegreeOfParallelism = 50 }, i =>
            {

                if (logLines[i] == "")
                    return;
                var parsedLine = ParseLine(logLines[i], i, DateTime.MinValue, false);

                if (parsedLine.Error == ErrorType.IncompleteLine)
                {
                    incompleteLines.Add(parsedLine);
                    return;
                }
                parsedLog[i] = parsedLine;
                parsedLog[i].LogName = combatLog.Name;

            });

            var cleanedLogs = parsedLog.Where(l => l != null);
            CombatTimestampRectifier.RectifyTimeStamps(cleanedLogs.ToList());
            var orderdedLog = cleanedLogs.OrderBy(l => l.TimeStamp);
            UpdateStateAndLogs(orderdedLog.ToList(), false);
            if (includeIncomplete)
            {
                var includedLines = orderdedLog.ToList();
                includedLines.AddRange(incompleteLines);
                orderdedLog = includedLines.OrderBy(l => l.TimeStamp);
            }
            return orderdedLog.ToList();
        }
        private static List<string> GetInfoComponents(string log)
        {
            var returnValues = new List<string>();
            int startIndex = 0;
            int numberOfCloses = 0;
            bool isOpen = false;
            bool needsSecondClose = false;
            for (var i = 0; i < log.Length; i++)
            {
                if (log[i] == '[' && !isOpen)
                {
                    startIndex = i + 1;
                    isOpen = true;
                    continue;
                }

                if (log[i] == '[' && isOpen)
                {
                    needsSecondClose = true;
                }
                if (log[i] == ']' && !needsSecondClose)
                {
                    isOpen = false;
                    returnValues.Add(log.Substring(startIndex, i - startIndex));
                    numberOfCloses++;
                    if (numberOfCloses == 5)
                        return returnValues;
                    else
                        continue;
                }

                if (log[i] == ']' && needsSecondClose)
                {
                    needsSecondClose = false;
                }
            }
            return returnValues;
        }
        private static void UpdateStateAndLogs(List<ParsedLogEntry> orderdedLog, bool realTime)
        {
            foreach (var line in orderdedLog)
            {
                CombatLogStateBuilder.UpdateCurrentStateWithSingleLog(line, realTime);
            }
        }
    }
}
