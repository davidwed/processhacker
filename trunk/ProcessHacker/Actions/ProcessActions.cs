﻿/*
 * Process Hacker - 
 *   process actions
 * 
 * Copyright (C) 2009 wj32
 * 
 * This file is part of Process Hacker.
 * 
 * Process Hacker is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Process Hacker is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Process Hacker.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Windows.Forms;
using Microsoft.Samples;

namespace ProcessHacker.UI
{
    public static class ProcessActions
    {
        private static bool Prompt(IWin32Window window, int[] pids, string[] names, 
            string action, string content, bool promptOnlyIfDangerous)
        {
            string name = "the selected process(es)";

            if (pids.Length == 1)
                name = names[0];
            else
                name = "the selected processes";

            bool dangerous = false;

            foreach (int pid in pids)
            {
                if (Misc.IsDangerousPid(pid))
                {
                    dangerous = true;
                    break;
                }
            }

            if (promptOnlyIfDangerous && !dangerous)
                return true;

            DialogResult result = DialogResult.No;

            if (Program.WindowsVersion == WindowsVersion.Vista)
            {
                TaskDialog td = new TaskDialog();

                td.WindowTitle = "Process Hacker";
                td.MainInstruction = "Do you want to " + action + " " + name + "?";
                td.Content = content;

                if (dangerous)
                {
                    td.MainIcon = TaskDialogIcon.Warning;
                    td.Content = "You are about to " + action + " one or more system processes. " +
                        "Doing so will cause system instability. Are you sure you want to continue?";
                }

                if (pids.Length > 1)
                {
                    td.ExpandFooterArea = true;
                    td.ExpandedInformation = "Processes:\r\n";

                    for (int i = 0; i < pids.Length; i++)
                    {
                        td.ExpandedInformation += names[i] + " (PID " + pids[i].ToString() + ")\r\n";
                    }

                    td.ExpandedInformation = td.ExpandedInformation.Trim();
                }

                td.Buttons = new TaskDialogButton[]
                {
                    new TaskDialogButton((int)DialogResult.Yes, char.ToUpper(action[0]) + action.Substring(1)),
                    new TaskDialogButton((int)DialogResult.No, "Cancel")
                };
                td.DefaultButton = (int)DialogResult.No;

                result = (DialogResult)td.Show(window);
            }
            else if (Program.WindowsVersion == WindowsVersion.XP)
            {
                result = MessageBox.Show("Are you sure you want to " + action + " " + name + "?",
                    "Process Hacker", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2);
            }

            return result == DialogResult.Yes;
        }

        private static bool ElevateIfRequired(IWin32Window window, int[] pids, string[] names,
            Win32.PROCESS_RIGHTS access, string action)
        {
            if (Program.WindowsVersion == WindowsVersion.Vista &&
                Program.ElevationType == Win32.TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited &&
                Program.KPH == null)
            {
                try
                {
                    foreach (int pid in pids)
                    {
                        using (var phandle = new Win32.ProcessHandle(pid, access))
                        { }
                    }
                }
                catch (WindowsException ex)
                {
                    TaskDialog td = new TaskDialog();

                    td.WindowTitle = "Process Hacker";
                    td.MainIcon = TaskDialogIcon.Shield;
                    td.MainInstruction = "Do you want to elevate the action?";
                    td.Content = "The action cannot be performed in the current security context. " +
                        "Do you want Process Hacker to prompt for the appropriate credentials and elevate the action?";

                    td.ExpandedInformation = "Error: " + ex.Message + " (0x" + ex.ErrorCode.ToString("x") + ")";
                    td.ExpandFooterArea = true;

                    td.Buttons = new TaskDialogButton[]
                    {
                        new TaskDialogButton((int)DialogResult.Yes, "Elevate\nPrompt for credentials and elevate the action."),
                        new TaskDialogButton((int)DialogResult.No, "Continue\nAttempt to perform the action without elevation.")
                    };
                    td.CommonButtons = TaskDialogCommonButtons.Cancel;
                    td.UseCommandLinks = true;
                    td.Callback = (taskDialog, args, userData) =>
                        {
                            if (args.Notification == TaskDialogNotification.Created)
                            {
                                taskDialog.SetButtonElevationRequiredState((int)DialogResult.Yes, true);
                            }

                            return false;
                        };

                    DialogResult result = (DialogResult)td.Show(window);

                    if (result == DialogResult.Yes)
                    {
                        string objects = "";

                        foreach (int pid in pids)
                            objects += pid + ",";

                        Program.StartProcessHackerAdmin("-e -type process -action " + action + " -obj \"" +
                            objects + "\" -hwnd " + window.Handle.ToString(), null, window.Handle);

                        return true;
                    }
                    else if (result == DialogResult.No)
                    {
                        return false;
                    }
                    else if (result == DialogResult.Cancel)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static void Terminate(IWin32Window window, int[] pids, string[] names, bool prompt)
        {
            if (ElevateIfRequired(window, pids, names, Win32.PROCESS_RIGHTS.PROCESS_TERMINATE, "terminate"))
                return;

            if (prompt && !Prompt(window, pids, names, "terminate",
                "Terminating a process will cause unsaved data to be lost. " +
                "Terminating a system process will cause system instability. " +
                "Are you sure you want to continue?", false))
                return;

            for (int i = 0; i < pids.Length; i++)
            {
                try
                {
                    using (Win32.ProcessHandle phandle = new Win32.ProcessHandle(pids[i],
                        Win32.PROCESS_RIGHTS.PROCESS_TERMINATE))
                        phandle.Terminate();
                }
                catch (Exception ex)
                {
                    DialogResult r = MessageBox.Show("Could not terminate process \"" + names[i] +
                        "\" with PID " + pids[i].ToString() + ":\n\n" +
                        ex.Message, "Process Hacker", MessageBoxButtons.OKCancel, MessageBoxIcon.Error);

                    if (r == DialogResult.Cancel)
                        return;
                }
            }
        }

        public static void Suspend(IWin32Window window, int[] pids, string[] names, bool prompt)
        {
            if (ElevateIfRequired(window, pids, names, Win32.PROCESS_RIGHTS.PROCESS_SUSPEND_RESUME, "suspend"))
                return;

            if (prompt && !Prompt(window, pids, names, "suspend",
                "Suspending a process will pause its execution. " +
                "Suspending a system process will cause system instability. " +
                "Are you sure you want to continue?", true))
                return;

            for (int i = 0; i < pids.Length; i++)
            {
                try
                {
                    using (Win32.ProcessHandle phandle = new Win32.ProcessHandle(pids[i],
                        Win32.PROCESS_RIGHTS.PROCESS_SUSPEND_RESUME))
                        phandle.Suspend();
                }
                catch (Exception ex)
                {
                    DialogResult r = MessageBox.Show("Could not suspend process \"" + names[i] +
                        "\" with PID " + pids[i].ToString() + ":\n\n" +
                        ex.Message, "Process Hacker", MessageBoxButtons.OKCancel, MessageBoxIcon.Error);

                    if (r == DialogResult.Cancel)
                        return;
                }
            }
        }

        public static void Resume(IWin32Window window, int[] pids, string[] names, bool prompt)
        {
            if (ElevateIfRequired(window, pids, names, Win32.PROCESS_RIGHTS.PROCESS_SUSPEND_RESUME, "resume"))
                return;

            if (prompt && !Prompt(window, pids, names, "resume",
                "Resuming a process will begin its execution. " +
                "Resuming a system process may lead to system instability. " +
                "Are you sure you want to continue?", true))
                return;

            for (int i = 0; i < pids.Length; i++)
            {
                try
                {
                    using (Win32.ProcessHandle phandle = new Win32.ProcessHandle(pids[i],
                        Win32.PROCESS_RIGHTS.PROCESS_SUSPEND_RESUME))
                        phandle.Resume();
                }
                catch (Exception ex)
                {
                    DialogResult r = MessageBox.Show("Could not resume process \"" + names[i] +
                        "\" with PID " + pids[i].ToString() + ":\n\n" +
                        ex.Message, "Process Hacker", MessageBoxButtons.OKCancel, MessageBoxIcon.Error);

                    if (r == DialogResult.Cancel)
                        return;
                }
            }
        }     

        public static void ReduceWorkingSet(IWin32Window window, int[] pids, string[] names, bool prompt)
        {
            if (ElevateIfRequired(window, pids, names,
                Win32.PROCESS_RIGHTS.PROCESS_QUERY_INFORMATION | Win32.PROCESS_RIGHTS.PROCESS_SET_QUOTA, 
                "reduceworkingset"))
                return;

            if (prompt && !Prompt(window, pids, names, "reduce the working set of",
                "Reducing the working set of a process reduces its physical memory consumption. " + 
                "Are you sure you want to continue?", true))
                return;

            for (int i = 0; i < pids.Length; i++)
            {
                try
                {
                    using (Win32.ProcessHandle phandle = new Win32.ProcessHandle(pids[i],
                        Win32.PROCESS_RIGHTS.PROCESS_QUERY_INFORMATION | Win32.PROCESS_RIGHTS.PROCESS_SET_QUOTA))
                        phandle.EmptyWorkingSet();
                }
                catch (Exception ex)
                {
                    DialogResult r = MessageBox.Show("Could not reduce the working set of process \"" + names[i] +
                        "\" with PID " + pids[i].ToString() + ":\n\n" +
                        ex.Message, "Process Hacker", MessageBoxButtons.OKCancel, MessageBoxIcon.Error);

                    if (r == DialogResult.Cancel)
                        return;
                }
            }
        }
    }
}
