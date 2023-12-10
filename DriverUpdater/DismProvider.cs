﻿/*
 * Copyright (c) The LumiaWOA and DuoWOA authors
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
using Microsoft.Dism;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace DriverUpdater
{
    internal class DismProvider : IDisposable
    {
        private bool disposedValue;
        private readonly DismSession session;

        public DismProvider(string DrivePath)
        {
            // Workaround for issue in DISM api
            if (!DrivePath.EndsWith("\\"))
            {
                DrivePath += "\\";
            }
            DismApi.Initialize(DismLogLevel.LogErrors);
            session = DismApi.OpenOfflineSession(DrivePath);
        }

        public uint AddOfflineDriver(string driverStoreFileName)
        {
            uint ntStatus = 0;

            try
            {
                DismApi.AddDriver(session, driverStoreFileName, false);
            }
            catch (Exception e)
            {
                ntStatus = (uint)e.HResult;
            }

            return ntStatus;
        }

        public uint GetInstalledOEMDrivers(out string[] existingDrivers)
        {
            List<string> lexistingDrivers = [];

            uint ntStatus = 0;

            try
            {
                foreach (DismDriverPackage driver in DismApi.GetDrivers(session, false))
                {
                    lexistingDrivers.Add(driver.PublishedName);
                }
            }
            catch (Exception e)
            {
                ntStatus = (uint)e.HResult;
            }

            existingDrivers = lexistingDrivers.ToArray();

            return ntStatus;
        }

        public uint RemoveOfflineDriver(string driverStoreFileName)
        {
            uint ntStatus = 0;

            try
            {
                DismApi.RemoveDriver(session, driverStoreFileName);
            }
            catch (Exception e)
            {
                ntStatus = (uint)e.HResult;
            }

            return ntStatus;
        }

        public bool InstallApps(List<string> deps)
        {
            Logging.Log("Installing App Packages...");

            IEnumerable<string> appPackages = deps.Where(x => !Path.GetDirectoryName(x).EndsWith(Path.DirectorySeparatorChar + "Frameworks"));

            long Progress = 0;
            DateTime startTime = DateTime.Now;

            foreach (string app in appPackages)
            {
                Console.Title = $"Driver Updater - Installing App Package - {app}";
                Logging.ShowProgress(Progress++, appPackages.Count(), startTime, false);

                const int maxAttempts = 3;
                int currentFails = 0;
                ulong ntStatus = 0;

                string appLicense = null;
                if (File.Exists(Path.Combine(Path.GetDirectoryName(app), $"{Path.GetFileNameWithoutExtension(app)}.xml")))
                {
                    appLicense = Path.Combine(Path.GetDirectoryName(app), $"{Path.GetFileNameWithoutExtension(app)}.xml");
                }

                while (currentFails < maxAttempts)
                {
                    try
                    {
                        DismApi.AddProvisionedAppxPackage(session, app, null, appLicense, null);
                    }
                    catch (Exception e)
                    {
                        ntStatus = (uint)e.HResult;
                    }

                    // Invalid ARG can be thrown when an issue happens with a specific driver inf
                    // No investigation done yet, but for now, this will do just fine
                    if ((ntStatus & 0x80000000) != 0 && ntStatus != 0xC1570118)
                    {
                        currentFails++;
                    }
                    else
                    {
                        break;
                    }
                }

                if ((ntStatus & 0x80000000) != 0 && ntStatus != 0xC1570118)
                {
                    Logging.Log("");
                    Logging.Log($"DismApi->AddProvisionedAppxPackage: ntStatus=0x{ntStatus:X8}, app={app}", Logging.LoggingLevel.Error);

                    return false;
                }
            }

            Logging.ShowProgress(appPackages.Count(), appPackages.Count(), startTime, false);
            Logging.Log("");

            return true;
        }

        public bool InstallDrivers(string DriverRepo, ReadOnlyCollection<string> definitionPaths)
        {
            Logging.Log("Installing new drivers...");

            foreach (string path in definitionPaths)
            {
                Logging.Log(path);

                // The where LINQ call is because directory can return .inf_ as well...
                IEnumerable<string> infFiles = Directory.EnumerateFiles($"{DriverRepo}\\{path}", "*.inf", SearchOption.AllDirectories)
                    .Where(x => x.EndsWith(".inf", StringComparison.InvariantCultureIgnoreCase));

                long Progress = 0;
                DateTime startTime = DateTime.Now;

                // Install every inf present in the component folder
                foreach (string inf in infFiles)
                {
                    // First add the driver package to the image
                    Console.Title = $"Driver Updater - DismApi->AddDriver - {inf}";
                    Logging.ShowProgress(Progress++, infFiles.Count(), startTime, false);

                    const int maxAttempts = 3;
                    int currentFails = 0;
                    ulong ntStatus = 0;

                    while (currentFails < maxAttempts)
                    {
                        ntStatus = AddOfflineDriver(inf);

                        // Invalid ARG can be thrown when an issue happens with a specific driver inf
                        // No investigation done yet, but for now, this will do just fine
                        if ((ntStatus & 0x80000000) != 0)
                        {
                            currentFails++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if ((ntStatus & 0x80000000) != 0)
                    {
                        Logging.Log("");
                        Logging.Log($"DismApi->AddDriver: ntStatus=0x{ntStatus:X8}, driverInf={inf}", Logging.LoggingLevel.Error);

                        return false;
                    }
                }
                Logging.ShowProgress(infFiles.Count(), infFiles.Count(), startTime, false);
                Logging.Log("");
            }

            return true;
        }

        public bool InstallDepApps(List<string> deps)
        {
            IEnumerable<string> appDependencyPackages = deps.Where(x => x.Replace(Path.DirectorySeparatorChar + Path.GetFileName(x), "").EndsWith(Path.DirectorySeparatorChar + "Frameworks"));

            Logging.Log("Installing App Framework Packages...");

            long Progress = 0;
            DateTime startTime = DateTime.Now;

            foreach (string app in appDependencyPackages)
            {
                Console.Title = $"Driver Updater - Installing App Framework Package - {app}";
                Logging.ShowProgress(Progress++, appDependencyPackages.Count(), startTime, false);

                const int maxAttempts = 3;
                int currentFails = 0;
                ulong ntStatus = 0;

                while (currentFails < maxAttempts)
                {
                    try
                    {
                        DismApi.AddProvisionedAppxPackage(session, app, null, null, null);
                    }
                    catch (Exception e)
                    {
                        ntStatus = (uint)e.HResult;
                    }

                    // Invalid ARG can be thrown when an issue happens with a specific driver inf
                    // No investigation done yet, but for now, this will do just fine
                    if ((ntStatus & 0x80000000) != 0 && ntStatus != 0xC1570118)
                    {
                        currentFails++;
                    }
                    else
                    {
                        break;
                    }
                }

                if ((ntStatus & 0x80000000) != 0 && ntStatus != 0xC1570118)
                {
                    Logging.Log("");
                    Logging.Log($"DismApi->AddProvisionedAppxPackage: ntStatus=0x{ntStatus:X8}, app={app}", Logging.LoggingLevel.Error);

                    return false;
                }
            }

            Logging.ShowProgress(appDependencyPackages.Count(), appDependencyPackages.Count(), startTime, false);
            Logging.Log("");

            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null

                session.Dispose();
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
