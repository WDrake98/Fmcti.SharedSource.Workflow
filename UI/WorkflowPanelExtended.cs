using System;
using System.Web.UI;
using Sitecore;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Security.AccessControl;
using Sitecore.SecurityModel;
using Sitecore.Shell.Applications.ContentManager.Panels;
using Sitecore.Shell.Framework.CommandBuilders;
using Sitecore.Shell.Framework.Commands;
using Sitecore.Web.UI.WebControls.Ribbons;
using Sitecore.Workflows;

namespace Fmcti.SharedSource.Workflow.UI
{
    public class WorkflowPanelExtended : WorkflowPanel
    {

        private Item checkInItem;

        private static bool? IsAcessRightAllowedOnWorkflowItem(Item item, AccessRight securityRight)
        {
            try
            {
                if (item.State.GetWorkflowState() != null)
                {
                    var currrentWfStateId = item.State.GetWorkflowState().StateID;
                    Item workflowStateItem = null;
                    using (new SecurityDisabler())
                    {
                        workflowStateItem = Database.GetDatabase("master").GetItem(currrentWfStateId);
                    }

                    if (workflowStateItem != null)
                        return AuthorizationManager.IsAllowed(workflowStateItem, securityRight, Context.User);
                }
            }
            catch (Exception e)
            {
                Log.Error("IsWorkflowStateWriteAllowed", e, typeof(WorkflowPanelExtended));
            }
            return null;
        }

        // Methods
        private static bool CanShowCommands(Item item, WorkflowCommand[] commands)
        {
            Assert.ArgumentNotNull(item, "item");
            if ((item.Appearance.ReadOnly || (commands == null)) || (commands.Length <= 0))
            {
                return false;
            }
            var workflowFlag = IsAcessRightAllowedOnWorkflowItem(item, AccessRight.WorkflowCommandExecute);

            if (workflowFlag != null)
            {
                return workflowFlag.Value;
            }

            if (!Context.IsAdministrator && (!item.Access.CanWriteLanguage() || (!item.Locking.CanLock() && !item.Locking.HasLock())))
            {
                return false;
            }
            return true;
        }

        private Item GetCheckInItem()
        {
            if (this.checkInItem == null)
            {
                this.checkInItem = Context.Database.Items["/sitecore/system/Settings/Workflow/Check In"];
            }
            return this.checkInItem;
        }

        private static void GetCommands(Item[] items, out IWorkflow workflow, out WorkflowCommand[] commands)
        {
            Assert.ArgumentNotNull(items, "items");
            Item item = items[0];
            if (item != null)
            {
                IWorkflowProvider workflowProvider = Context.ContentDatabase.WorkflowProvider;
                if ((workflowProvider != null) && (workflowProvider.GetWorkflows().Length > 0))
                {
                    workflow = workflowProvider.GetWorkflow(item);
                    if ((workflow != null) && (workflow.GetState(item) != null))
                    {
                        commands = WorkflowFilterer.FilterVisibleCommands(workflow.GetCommands(item));
                        return;
                    }
                }
            }
            workflow = null;
            commands = null;
        }

        private static string GetText(Item[] items)
        {
            Assert.ArgumentNotNull(items, "items");
            if ((items.Length <= 0) || (items.Length != 1))
            {
                return string.Empty;
            }
            Item entity = items[0];
            if (entity.Appearance.ReadOnly)
            {
                return string.Empty;
            }
            if (AuthorizationManager.IsAllowed(entity, AccessRight.ItemWrite, Context.User))
            {
                if (entity.Locking.HasLock())
                {
                    return Translate.Text("<b>You</b> have locked this item.");
                }
                if (entity.Locking.IsLocked())
                {
                    return Translate.Text("<b>\"{0}\"</b> has locked this item.", new object[] { StringUtil.GetString(new string[] { entity.Locking.GetOwnerWithoutDomain(), "?" }) });
                }
                if (entity.Locking.CanLock())
                {
                    return Translate.Text("Click Edit to lock and edit this item.");
                }
                IWorkflow workflow = entity.State.GetWorkflow();
                WorkflowState state = entity.State.GetWorkflowState();
                if ((workflow == null) || (state == null))
                {
                    return Translate.Text("You do not have permission to<br/>edit the content of this item.");
                }
                if (state.FinalState)
                {
                    return Translate.Text("This item has been approved.");
                }
                return Translate.Text("The item is in the <b>{0}</b> state<br/>in the <b>{1}</b> workflow.", new object[] { StringUtil.GetString(new string[] { state.DisplayName, "?" }), StringUtil.GetString(new string[] { workflow.Appearance.DisplayName, "?" }) });
            }
            if (entity.Access.CanWrite())
            {
                return Translate.Text("Click Edit to lock and edit this item.");
            }
            IWorkflow workflow2 = entity.State.GetWorkflow();
            WorkflowState workflowState = entity.State.GetWorkflowState();
            if ((workflow2 == null) || (workflowState == null))
            {
                return Translate.Text("You do not have permission to<br/>edit the content of this item.");
            }
            if (workflowState.FinalState)
            {
                return Translate.Text("This item has been approved.");
            }
            return Translate.Text("The item is in the <b>{0}</b> state<br/>in the <b>{1}</b> workflow.", new object[] { StringUtil.GetString(new string[] { workflowState.DisplayName, "?" }), StringUtil.GetString(new string[] { workflow2.Appearance.DisplayName, "?" }) });
        }

        private bool IsCommandEnabled(string command, Item item)
        {
            Assert.ArgumentNotNullOrEmpty(command, "command");
            Assert.ArgumentNotNull(item, "item");
            CommandState state = CommandManager.QueryState(command, item);
            if (state != CommandState.Down)
            {
                return (state == CommandState.Enabled);
            }
            return true;
        }

        public override void Render(HtmlTextWriter output, Ribbon ribbon, Item button, CommandContext context)
        {
            Assert.ArgumentNotNull(output, "output");
            Assert.ArgumentNotNull(ribbon, "ribbon");
            Assert.ArgumentNotNull(button, "button");
            Assert.ArgumentNotNull(context, "context");
            if (context.Items.Length >= 1)
            {
                Item item = context.Items[0];
                if (base.HasField(item, FieldIDs.Workflow))
                {
                    IWorkflow workflow;
                    WorkflowCommand[] commandArray;
                    GetCommands(context.Items, out workflow, out commandArray);
                    bool flag = this.IsCommandEnabled("item:checkout", item);
                    bool flag2 = CanShowCommands(item, commandArray);
                    bool flag3 = this.IsCommandEnabled("item:checkin", item);
                    base.RenderText(output, GetText(context.Items));
                    if (((workflow != null) || flag) || (flag2 || flag3))
                    {
                        Context.ClientPage.ClientResponse.DisableOutput();
                        ribbon.BeginSmallButtons(output);
                        if (flag)
                        {
                            base.RenderSmallButton(output, ribbon, string.Empty, Translate.Text("Edit"), "Applications/24x24/document_edit.png", Translate.Text("Start editing this item."), "item:checkout", base.Enabled, false);
                        }
                        if (flag3)
                        {
                            Item checkInItem = this.GetCheckInItem();
                            if (checkInItem != null)
                            {
                                base.RenderSmallButton(output, ribbon, string.Empty, checkInItem["Phrase"], checkInItem.Appearance.Icon, Translate.Text("Check this item in."), string.Concat(new object[] { "item:checkin(id=", item.ID, ",language=", item.Language.Name, ",version=", item.Version, ")" }), base.Enabled, false);
                            }
                        }

                        if (flag2)
                        {
                            foreach (WorkflowCommand command in commandArray)
                            {
                                base.RenderSmallButton(output, ribbon, string.Empty, command.DisplayName, command.Icon, command.DisplayName, new WorkflowCommandBuilder(item, workflow, command).ToString(), base.Enabled, false);
                            }
                        }


                        if (workflow != null)
                        {
                            base.RenderSmallButton(output, ribbon, Sitecore.Web.UI.HtmlControls.Control.GetUniqueID("B"), Translate.Text("History"), "Applications/16x16/history.png", Translate.Text("Show the workflow history."), "item:workflowhistory", base.Enabled, false);
                        }
                        
                        ribbon.EndSmallButtons(output);
                        Context.ClientPage.ClientResponse.EnableOutput();
                    }
                }
            }
        }

    }


}
