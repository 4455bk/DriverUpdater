﻿using DiscUtils.Registry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DriverUpdater
{
    public class RegistryFixer
    {
        public static void FixLeftOvers(string DrivePath)
        {
            _ = ModifyRegistryForLeftOvers(Path.Combine(DrivePath, "Windows\\System32\\config\\SYSTEM"), Path.Combine(DrivePath, "Windows\\System32\\config\\SOFTWARE"));
        }

        private static void FixDriverStorePathsInRegistryValueForLeftOvers(RegistryKey registryKey, string registryValue)
        {
            Regex regex = new("(.*oem[0-9]+\\.inf.*)|(.*(QCOM|MSHW|VEN_QCOM&DEV_|VEN_MSHW&DEV_)[0-9A-F]{4}.*)|(.*surface.*duo.*inf)|(.*\\\\qc.*)|(.*\\\\surface.*)");
            Regex antiRegex = new("(.*QCOM((2465)|(2466)|(2484)|(2488)|(24A5)|(24B6)|(24B7)|(24BF)|(7002)|(FFE1)|(FFE2)|(FFE3)|(FFE4)|(FFE5)).*)|(.*qcap.*)|(.*qcursext.*)");

            // TODO:
            // Key: Microsoft\Windows\CurrentVersion\Setup\PnpResources\Registry\HKLM\ ... \
            // Val: Owners

            if (registryKey?.GetValueNames().Any(x => x.Equals(registryValue, StringComparison.InvariantCultureIgnoreCase)) == true)
            {
                switch (registryKey.GetValueType(registryValue))
                {
                    case RegistryValueType.String:
                        {
                            string og = (string)registryKey.GetValue(registryValue);

                            if (regex.IsMatch(og) && !antiRegex.IsMatch(og))
                            {
                                string currentValue = regex.Match(og).Value;

                                Logging.Log($"Deleting {currentValue} in {registryKey.Name}\\{registryValue}");

                                if (registryValue == "")
                                {
                                    registryValue = null;
                                }

                                registryKey.DeleteValue(registryValue);
                            }

                            break;
                        }
                    case RegistryValueType.ExpandString:
                        {
                            string og = (string)registryKey.GetValue(registryValue);

                            if (regex.IsMatch(og) && !antiRegex.IsMatch(og))
                            {
                                string currentValue = regex.Match(og).Value;

                                Logging.Log($"Deleting {currentValue} in {registryKey.Name}\\{registryValue}");

                                if (registryValue == "")
                                {
                                    registryValue = null;
                                }

                                registryKey.DeleteValue(registryValue);
                            }

                            break;
                        }
                    case RegistryValueType.MultiString:
                        {
                            string[] ogvals = (string[])registryKey.GetValue(registryValue);
                            List<string> newVals = [];

                            bool updated = false;

                            foreach (string og in ogvals)
                            {
                                if (regex.IsMatch(og) && !antiRegex.IsMatch(og))
                                {
                                    string currentValue = regex.Match(og).Value;

                                    Logging.Log($"Deleting {currentValue} in {registryKey.Name}\\{registryValue}");
                                    updated = true;
                                }
                                else
                                {
                                    newVals.Add(og);
                                }
                            }

                            if (updated)
                            {
                                if (newVals.Any())
                                {
                                    registryKey.SetValue(registryValue, newVals.ToArray(), RegistryValueType.MultiString);
                                }
                                else
                                {
                                    if (registryValue == "")
                                    {
                                        registryValue = null;
                                    }

                                    registryKey.DeleteValue(registryValue);
                                }
                            }

                            break;
                        }
                }
            }
        }

        private static void CrawlInRegistryKeyForLeftOvers(RegistryKey registryKey)
        {
            if (registryKey != null)
            {
                Regex regex = new("(.*oem[0-9]+\\.inf.*)|(.*(QCOM|MSHW|VEN_QCOM&DEV_|VEN_MSHW&DEV_)[0-9A-F]{4}.*)|(.*surface.*duo.*inf)|(.*\\\\qc.*)|(.*\\\\surface.*)");
                Regex antiRegex = new("(.*QCOM((2465)|(2466)|(2484)|(2488)|(24A5)|(24B6)|(24B7)|(24BF)|(7002)|(FFE1)|(FFE2)|(FFE3)|(FFE4)|(FFE5)).*)|(.*qcap.*)|(.*qcursext.*)");

                foreach (string subRegistryValue in registryKey.GetValueNames())
                {
                    if (regex.IsMatch(subRegistryValue) && !antiRegex.IsMatch(subRegistryValue))
                    {
                        Logging.Log($"Deleting {registryKey.Name}\\{subRegistryValue}");
                        registryKey.DeleteValue(subRegistryValue);

                        continue;
                    }

                    FixDriverStorePathsInRegistryValueForLeftOvers(registryKey, subRegistryValue);
                }

                foreach (string subRegistryKey in registryKey.GetSubKeyNames())
                {
                    if (regex.IsMatch(subRegistryKey) && !antiRegex.IsMatch(subRegistryKey))
                    {
                        Logging.Log($"Deleting {registryKey.Name}\\{subRegistryKey}");
                        registryKey.DeleteSubKeyTree(subRegistryKey);

                        continue;
                    }

                    CrawlInRegistryKeyForLeftOvers(registryKey.OpenSubKey(subRegistryKey));
                }
            }
        }

        internal static bool ModifyRegistryForLeftOvers(string systemHivePath, string softwareHivePath)
        {
            try
            {
                using (RegistryHive hive = new(
                    File.Open(
                        systemHivePath,
                        FileMode.Open,
                        FileAccess.ReadWrite
                    ), DiscUtils.Streams.Ownership.Dispose))
                {
                    Logging.Log("Processing SYSTEM hive");
                    CrawlInRegistryKeyForLeftOvers(hive.Root);
                }

                using (RegistryHive hive = new(
                    File.Open(
                        softwareHivePath,
                        FileMode.Open,
                        FileAccess.ReadWrite
                    ), DiscUtils.Streams.Ownership.Dispose))
                {
                    Logging.Log("Processing SOFTWARE hive");
                    CrawlInRegistryKeyForLeftOvers(hive.Root);
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static void FixRegistryPaths(string DrivePath)
        {
            // Windows DriverStore directory is located at \Windows\System32\DriverStore\FileRepository
            // Drivers are named using the following way acpi.inf_amd64_f8b60f94eae135e9
            // While the first and second parts of these names are straight forward (driver inf name + architecture),
            // The other part is dependent on the driver signature and thus, if the driver changes, will change.
            // Usually this isn't a problem but uninstalling drivers can often leave left overs in the registry, pointing to now incorrect paths
            // More over, some drivers will crash if these paths are unfixed. Let's attempt to fix them here.

            // First start by getting a list of all driver folder names right now.
            string DriverStorePath = Path.Combine(DrivePath, "Windows\\System32\\DriverStore\\FileRepository");
            string[] Folders = Directory.EnumerateDirectories(DriverStorePath).Where(x => Directory.EnumerateFiles(x, "*.cat").Any()).Select(x => x.Split('\\').Last()).ToArray();

            // Now, create a new array of all folder names, but without the hash dependent part.
            Regex[] folderRegexes = Folders.Select(BuildRegexForDriverStoreFolderName).ToArray();

            // Now that this is done, process the hives.
            _ = ModifyRegistry(Path.Combine(DrivePath, "Windows\\System32\\config\\SYSTEM"), Path.Combine(DrivePath, "Windows\\System32\\config\\SOFTWARE"), Folders, folderRegexes);
        }

        private static Regex BuildRegexForDriverStoreFolderName(string driverStoreFolderName)
        {
            string neutralDriverStoreFolderName = string.Join('_', driverStoreFolderName.Split('_')[..driverStoreFolderName.Count(y => y == '_')]) + '_';
            string escapedNeutralDriverStoreFolderName = Regex.Escape(neutralDriverStoreFolderName);

            return new Regex(escapedNeutralDriverStoreFolderName + "[a-fA-F0-9]{16}");
        }

        private static void FixDriverStorePathsInRegistryValue(RegistryKey registryKey, string registryValue, string[] currentDriverStoreNames, Regex[] folderRegexes)
        {
            if (registryKey?.GetValueNames().Any(x => x.Equals(registryValue, StringComparison.InvariantCultureIgnoreCase)) == true)
            {
                switch (registryKey.GetValueType(registryValue))
                {
                    case RegistryValueType.String:
                        {
                            string og = (string)registryKey.GetValue(registryValue);

                            for (int i = 0; i < folderRegexes.Length; i++)
                            {
                                Regex regex = folderRegexes[i];
                                string matchingString = currentDriverStoreNames[i];

                                if (regex.IsMatch(og))
                                {
                                    string currentValue = regex.Match(og).Value;

                                    if (currentValue != matchingString)
                                    {
                                        Logging.Log($"Updated {currentValue} to {matchingString} in {registryKey.Name}\\{registryValue}");

                                        og = og.Replace(currentValue, matchingString);
                                        registryKey.SetValue(registryValue, og, RegistryValueType.String);
                                        break;
                                    }
                                }
                            }

                            break;
                        }
                    case RegistryValueType.ExpandString:
                        {
                            string og = (string)registryKey.GetValue(registryValue);

                            for (int i = 0; i < folderRegexes.Length; i++)
                            {
                                Regex regex = folderRegexes[i];
                                string matchingString = currentDriverStoreNames[i];

                                if (regex.IsMatch(og))
                                {
                                    string currentValue = regex.Match(og).Value;

                                    if (currentValue != matchingString)
                                    {
                                        Logging.Log($"Updated {currentValue} to {matchingString} in {registryKey.Name}\\{registryValue}");

                                        og = og.Replace(currentValue, matchingString);
                                        registryKey.SetValue(registryValue, og, RegistryValueType.ExpandString);
                                        break;
                                    }
                                }
                            }

                            break;
                        }
                    case RegistryValueType.MultiString:
                        {
                            string[] ogvals = (string[])registryKey.GetValue(registryValue);

                            bool updated = false;

                            for (int j = 0; j < ogvals.Length; j++)
                            {
                                string og = ogvals[j];

                                for (int i = 0; i < folderRegexes.Length; i++)
                                {
                                    Regex regex = folderRegexes[i];
                                    string matchingString = currentDriverStoreNames[i];

                                    if (regex.IsMatch(og))
                                    {
                                        string currentValue = regex.Match(og).Value;

                                        if (currentValue != matchingString)
                                        {
                                            Logging.Log($"Updated {currentValue} to {matchingString} in {registryKey.Name}\\{registryValue}");

                                            og = og.Replace(currentValue, matchingString);
                                            ogvals[j] = og;
                                            updated = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (updated)
                            {
                                registryKey.SetValue(registryValue, ogvals, RegistryValueType.MultiString);
                            }

                            break;
                        }
                }
            }
        }

        private static void CrawlInRegistryKey(RegistryKey registryKey, string[] currentDriverStoreNames, Regex[] folderRegexes)
        {
            if (registryKey != null)
            {
                foreach (string subRegistryValue in registryKey.GetValueNames())
                {
                    FixDriverStorePathsInRegistryValue(registryKey, subRegistryValue, currentDriverStoreNames, folderRegexes);
                }
                foreach (RegistryKey subRegistryKey in registryKey.SubKeys)
                {
                    CrawlInRegistryKey(subRegistryKey, currentDriverStoreNames, folderRegexes);
                }
            }
        }

        internal static bool ModifyRegistry(string systemHivePath, string softwareHivePath, string[] currentDriverStoreNames, Regex[] folderRegexes)
        {
            try
            {
                using (RegistryHive hive = new(
                    File.Open(
                        systemHivePath,
                        FileMode.Open,
                        FileAccess.ReadWrite
                    ), DiscUtils.Streams.Ownership.Dispose))
                {
                    Logging.Log("Processing SYSTEM hive");
                    CrawlInRegistryKey(hive.Root, currentDriverStoreNames, folderRegexes);
                }

                using (RegistryHive hive = new(
                    File.Open(
                        softwareHivePath,
                        FileMode.Open,
                        FileAccess.ReadWrite
                    ), DiscUtils.Streams.Ownership.Dispose))
                {
                    Logging.Log("Processing SOFTWARE hive");
                    CrawlInRegistryKey(hive.Root, currentDriverStoreNames, folderRegexes);
                }
            }
            catch
            {
                return false;
            }
            return true;
        }
    }
}