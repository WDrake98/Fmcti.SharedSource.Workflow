using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Text;
using System.Web;
using Sitecore;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Links;
using Sitecore.Security;
using Sitecore.Security.Accounts;
using Sitecore.SecurityModel;
using Sitecore.Workflows;
using Sitecore.Workflows.Simple;

namespace Fmcti.SharedSource.Workflow.Commands
{
    public class EmailCommand
    {
        private string _hostName;
        private WorkflowPipelineArgs _workflowPipelineArgs;
        private Item _dataItem;
        private Item _emailActionItem;
        private readonly Database _contextDatabase = Database.GetDatabase("master");

        public enum ContentEditorMode
        {
            Editor,
            Preview,
            Submit,
            SubmitComment,
            WorkBox,
            Production
        }

        public struct ItemWorkflowHistory
        {
            public DateTime ItemDateTime { get; set; }
            public String User { get; set; }
            public String PreviousState { get; set; }
            public String CurrentState { get; set; }
            public String Comment { get; set; }
        }

        public string HostName
        {
            get { return (_hostName = _hostName ?? HttpContext.Current.Request.Url.Host); }
            set { _hostName = value; }
        }

        public Item EmailActionItem
        {
            get
            {
                return _emailActionItem ?? (_emailActionItem = _workflowPipelineArgs.ProcessorItem == null ? null : _workflowPipelineArgs.ProcessorItem.InnerItem);
            }
        }

        public void Process(WorkflowPipelineArgs args)
        {
            _workflowPipelineArgs = args;
            _dataItem = _workflowPipelineArgs.DataItem;

            SendEmail();
        }

        public void SendEmail()
        {
            try
            {
                Assert.ArgumentNotNull(_workflowPipelineArgs, "_workflowPipelineArgs");

                if (EmailActionItem == null) return;

                HostName = GetText(EmailActionItem, "Host Name");

                var fullPath = EmailActionItem.Paths.FullPath;
                var assertTextFormat = "The '{0}' field is not specified in the mail action item: " + fullPath;

                var from = GetText(EmailActionItem, "from");
                var cc = GetText(EmailActionItem, "Cc");
                var subject = GetText(EmailActionItem, "subject");
                var message = GetText(EmailActionItem, "message");
                var mailHost = GetText(EmailActionItem, "mail server");
                var to = GetText(EmailActionItem, "to").Trim();

                Error.Assert(to.Length > 0, String.Format(assertTextFormat, "To"));
                Error.Assert(from.Length > 0, String.Format(assertTextFormat, "From"));
                Error.Assert(subject.Length > 0, String.Format(assertTextFormat, "Subject"));
                Error.Assert(HostName.Length > 0, String.Format(assertTextFormat, "Host Name"));

                var mailMessage = new MailMessage()
                {
                    From = new MailAddress(from),
                    Subject = subject,
                    Body = message,
                    IsBodyHtml = true
                };

                if (!String.IsNullOrEmpty(to))
                {
                    foreach (var toAddress in to.Split(new[] { ",", ";" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        mailMessage.To.Add(toAddress);
                    }
                }

                if (!String.IsNullOrEmpty(cc))
                {
                    foreach (var ccAddress in cc.Split(new[] { ",", ";" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        mailMessage.CC.Add(ccAddress);
                    }
                }

                new SmtpClient(mailHost).Send(mailMessage);
                Log.Info(String.Format("Email Sent To:{0} From:{1} Subject:{2} ", to, from, subject), this);
            }
            catch (Exception e)
            {
                Log.Error("Send Email", e, this);
            }
        }

        private string GetText(Item commandItem, string field)
        {
            var textValue = commandItem[field];
            return textValue.Length <= 0 ? null : ReplaceVariables(textValue);
        }

        private IEnumerable<ItemWorkflowHistory> GetWorkflowHistory()
        {
            var workflowHistory = new List<ItemWorkflowHistory>();

            try
            {
                var context = _contextDatabase;

                var workflowItemHistory = _dataItem.State.GetWorkflow().GetHistory(_dataItem);

                foreach (var workflowEvent in workflowItemHistory)
                {
                    var iItemPreviousState = context.GetItem(workflowEvent.OldState);
                    var iItemCurrentState = context.GetItem(workflowEvent.NewState);

                    var previousStateName = (iItemPreviousState != null) ? iItemPreviousState.DisplayName : string.Empty;
                    var currentStateName = (iItemCurrentState != null) ? iItemCurrentState.DisplayName : string.Empty;

                    workflowHistory.Add(new ItemWorkflowHistory()
                    {
                        ItemDateTime = workflowEvent.Date,
                        User = workflowEvent.User,
                        PreviousState = previousStateName,
                        CurrentState = currentStateName,
                        Comment = workflowEvent.Text
                    });
                }

                workflowHistory.Add(GetCurrentWorkflowHistoryItem());
            }
            catch (Exception e)
            {
                Log.Error("EmailCommand.Process", e, this);
            }
            return workflowHistory;
        }

        private ItemWorkflowHistory GetCurrentWorkflowHistoryItem()
        {
            var correctWorkflowState = GetCorrectWorkflowState();
            var currentState = correctWorkflowState == null ? string.Empty : correctWorkflowState.DisplayName;

            var history = new ItemWorkflowHistory()
            {
                ItemDateTime = DateTime.Now,
                User = Context.GetUserName(),
                PreviousState = _dataItem.State.GetWorkflowState().DisplayName,
                CurrentState = currentState,
                Comment = _workflowPipelineArgs.Comments
            };

            return history;
        }

        private string GetItemWorkflowName()
        {
            var itemWorkflow = _contextDatabase.WorkflowProvider.GetWorkflow(_dataItem);
            var itemWorkflowName = itemWorkflow.Appearance.DisplayName;

            return itemWorkflowName;
        }

        private static string GenerateWorkflowTableData(IEnumerable<ItemWorkflowHistory> workflowHistory)
        {
            var htmlWorkflowTable = new StringBuilder("<table><tr><th style='text-align: left; padding: 10px;'>Date</th><th style='text-align: left; padding: 10px;'>User</th><th style='text-align: left; padding: 10px;'>Previous State</th><th style='text-align: left; padding: 10px;'>Current State</th><th style='text-align: left; padding: 10px;'>Comment</th></tr>");
            const string htmlTdFormat = "<td style='text-align: left; padding: 10px;'>{0}</td>\n";

            foreach (var workflowItem in workflowHistory)
            {
                htmlWorkflowTable.AppendLine("<tr>");
                htmlWorkflowTable.AppendFormat(htmlTdFormat, workflowItem.ItemDateTime.ToString("dd MMMM yyyy, HH:mm:ss"));
                htmlWorkflowTable.AppendFormat(htmlTdFormat, workflowItem.User);
                htmlWorkflowTable.AppendFormat(htmlTdFormat, workflowItem.PreviousState);
                htmlWorkflowTable.AppendFormat(htmlTdFormat, workflowItem.CurrentState);
                htmlWorkflowTable.AppendFormat(htmlTdFormat, workflowItem.Comment);
                htmlWorkflowTable.AppendLine("</tr>");
            }
            htmlWorkflowTable.AppendLine("</table>");

            return htmlWorkflowTable.ToString();
        }

        private string ReplaceVariables(string text)
        {
            try
            {
                WorkflowState correctState = null;

                if (text.Contains("$itempath$")) text = text.Replace("$itempath$", _dataItem.Paths.FullPath);
                if (text.Contains("$itemlanguage$")) text = text.Replace("$itemlanguage$", _dataItem.Language.GetDisplayName());
                if (text.Contains("$itemversion$")) text = text.Replace("$itemversion$", _dataItem.Version.ToString());
                if (text.Contains("$itemtitle$")) text = text.Replace("$itemtitle$", _dataItem.DisplayName);
                if (text.Contains("$itemdatetime$")) text = text.Replace("$itemdatetime$", DateTime.Now.ToString("ddd, MMMM dd, yyyy, HH:mm:ss"));
                if (text.Contains("$itemworkflowname$")) text = text.Replace("$itemworkflowname$", GetItemWorkflowName());

                if (text.Contains("$itemcomment$")) text = text.Replace("$itemcomment$", _workflowPipelineArgs.Comments);

                if (text.Contains("$editlink$")) text = text.Replace("$editlink$", GetContentEditorLink(ContentEditorMode.Editor));
                if (text.Contains("$previewlink$")) text = text.Replace("$previewlink$", GetContentEditorLink(ContentEditorMode.Preview));
                if (text.Contains("$workboxLink$")) text = text.Replace("$workboxLink$", GetContentEditorLink(ContentEditorMode.WorkBox));
                if (text.Contains("$productionlink$")) text = text.Replace("$productionlink$", GetContentEditorLink(ContentEditorMode.Production));

                if (text.Contains("$itemworkflowstate$"))
                {
                    correctState = GetCorrectWorkflowState();
                    text = text.Replace("$itemworkflowstate$", correctState.DisplayName);
                }

                if (text.Contains("$commands$"))
                {
                    if (correctState == null) GetCorrectWorkflowState();
                    var commands = GetCommandLinks(correctState);
                    text = text.Replace("$commands$", commands);
                }

                if (text.Contains("$SubmittedByEmail$") || text.Contains("$SubmittedByName$"))
                {
                    var user = GetLastUser();
                    if (user != null)
                    {
                        text = text.Replace("$SubmittedByEmail$", user.Email);
                        text = text.Replace("$SubmittedByName$", user.FullName);
                    }
                }

                if (text.Contains("$LoggedInUserEmail$") || text.Contains("$LoggedInUserName$"))
                {
                    var user = User.FromName(Context.User.DisplayName, false);
                    if (user != null && user.Profile != null)
                    {
                        text = text.Replace("$LoggedInUserEmail$", user.Profile.Email);
                        text = text.Replace("$LoggedInUserName$", user.Profile.FullName);
                    }
                }

                if (text.Contains("$workflowhistorytable$")) text = text.Replace("$workflowhistorytable$", GenerateWorkflowTableData(GetWorkflowHistory()));
            }
            catch (Exception e)
            {
                Log.Error("ReplaceVariables", e, this);
            }

            return text;
        }

        private string GetCommandLinks(WorkflowState state)
        {
            var sb = new StringBuilder("<ul>");

            try
            {
                var workflow = _contextDatabase.WorkflowProvider.GetWorkflow(_dataItem);
                WorkflowCommand[] commands = null;
                using (new SecurityDisabler())
                {
                    commands = workflow.GetCommands(state.StateID);
                }
                if (commands != null)
                {
                    foreach (var command in commands)
                    {
                        var submitURL = GetContentEditorLink(ContentEditorMode.Submit, new ID(command.CommandID));
                        var submitCommentURL = GetContentEditorLink(ContentEditorMode.SubmitComment, new ID(command.CommandID));
                        sb.AppendFormat(@"<li><a href=""{0}"">{1}</a> or <a href=""{2}"">{1} & Comment</a></li>", submitURL, command.DisplayName, submitCommentURL);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("GetCommandLinks", e, this);
            }

            sb.Append("</ul>");

            return sb.ToString();
        }

        private WorkflowState GetCorrectWorkflowState()
        {
            try
            {
                var command = EmailActionItem.Parent;

                var nextStateId = command["Next state"];

                var itemWorkflow = _contextDatabase.WorkflowProvider.GetWorkflow(_workflowPipelineArgs.DataItem);

                return itemWorkflow.GetState(nextStateId);
            }
            catch (Exception e)
            {
                Log.Error("GetCorrectWorkflowState", e, this);
                return null;
            }
        }

        private UserProfile GetLastUser()
        {
            var contentItem = _workflowPipelineArgs.DataItem;
            var contentWorkflow = contentItem.Database.WorkflowProvider.GetWorkflow(contentItem);
            var contentHistory = contentWorkflow.GetHistory(contentItem);

            if (contentHistory.Length > 0)
            {
                var lastUser = contentHistory[contentHistory.Length - 1].User;
                var user = User.FromName(lastUser, false);
                if (user != null)
                    return user.Profile;
            }

            return null;
        }

        public string GetContentEditorLink(ContentEditorMode contentEditorMode, ID commandId = null)
        {
            var item = _dataItem;
            var hostWithoutScheme = HostName.Replace("http://", "");
            var defaultQueryStrings = "&id={" + item.ID + "}&la=" + item.Language.Name + "&v=" + item.Version.Number;
            var strURLFormat = string.Format("http://{0}/sitecore/shell/{{0}}{1}", hostWithoutScheme, defaultQueryStrings);

            try
            {
                switch (contentEditorMode)
                {
                    case ContentEditorMode.Editor:
                        return (string.Format(strURLFormat, "Applications/Content%20editor.aspx?fo=" + item.ID + "&sc_bw=1"));

                    case ContentEditorMode.Preview:
                        return (string.Format(strURLFormat, "feeds/action.aspx?c=Preview"));

                    case ContentEditorMode.Submit:
                        return (string.Format(strURLFormat, "feeds/action.aspx?c=Workflow&cmd=" + commandId));

                    case ContentEditorMode.SubmitComment:
                        return (string.Format(strURLFormat, "feeds/action.aspx?c=Workflow&cmd=" + commandId + "&nc=1"));

                    case ContentEditorMode.WorkBox:
                        return (string.Format(strURLFormat, "Applications/Workbox/Default.aspx?"));

                    case ContentEditorMode.Production:
                        var oldSiteName = Context.GetSiteName();
                        Context.SetActiveSite("website");

                        var url = string.Format("http://{0}{1}", hostWithoutScheme, LinkManager.GetItemUrl(item));

                        Context.SetActiveSite(oldSiteName);

                        return url;
                }
            }
            catch (Exception e)
            {
                Log.Error("GetContentEditorLink", e, this);
            }
            return "";
        }
    }
}
