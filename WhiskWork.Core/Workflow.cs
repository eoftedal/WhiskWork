using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Diagnostics;

namespace WhiskWork.Core
{
    public class Workflow
    {
        private readonly AdvancedWorkflowRepository _workflowRepository;
        private readonly IWorkItemRepository _workItemRepository;
        private readonly WorkStepQuery _workStepQuery;
        private readonly WorkItemQuery _workItemQuery;

        public Workflow(IWorkflowRepository workflowRepository, IWorkItemRepository workItemRepository)
        {
            _workflowRepository = new AdvancedWorkflowRepository(workflowRepository);
            _workItemRepository = workItemRepository;
            _workStepQuery = new WorkStepQuery(workflowRepository);
            _workItemQuery = new WorkItemQuery(workflowRepository, workItemRepository);
        }

        public IEnumerable<WorkItem> GetWorkItems(string path)
        {
            return _workItemRepository.GetWorkItems(path).Where(wi => wi.Status != WorkItemStatus.ParallelLocked);
        }

        public void CreateWorkItem(string id, string path)
        {
            var leafStep = _workStepQuery.GetLeafStep(path);

            if(leafStep.Type!=WorkStepType.Begin)
            {
                throw new InvalidOperationException("Can only create work items in begin step");
            }

            var classes = _workStepQuery.GetWorkItemClasses(leafStep);

            var newWorkItem = WorkItem.New(id, leafStep.Path, classes);

            WorkStep transientStep;
            if(_workStepQuery.IsWithinTransientStep(leafStep, out transientStep))
            {
                var workItems = _workItemRepository.GetWorkItems(transientStep.Path);
                Debug.Assert(workItems.Count()==1);

                var parentItem = workItems.ElementAt(0);
                _workItemRepository.UpdateWorkItem(parentItem.UpdateStatus(WorkItemStatus.ExpandLocked));

                newWorkItem = newWorkItem.MoveTo(leafStep).UpdateParent(parentItem);

                foreach (var workItemClass in newWorkItem.Classes)
                {
                    foreach (var rootClass in WorkItemClass.FindRootClasses(workItemClass))
                    {
                        newWorkItem = newWorkItem.AddClass(rootClass);
                    }
                }
            }
            else if(_workStepQuery.IsWithinExpandStep(leafStep))
            {
                throw new InvalidOperationException("Cannot create item directly under expand step");
            }

            newWorkItem = newWorkItem.UpdateOrdinal(_workItemQuery.GetNextOrdinal(newWorkItem));

            _workItemRepository.CreateWorkItem(newWorkItem);
        }

        public void UpdateWorkItem(string id, string path, NameValueCollection properties)
        {
            WorkItem workItem;
            if(!_workItemQuery.TryLocateWorkItem(id, out workItem))
            {
                throw new ArgumentException("Work item was not found");
            }

            WorkStep leafStep = _workStepQuery.GetLeafStep(path);

            if(workItem.Path!=leafStep.Path)
            {
                MoveWorkItem(workItem, leafStep);
            }
            if(properties.Count>0)
            {
                UpdateProperties(id, properties);
            }
        }

        private static void UpdateProperties(string id, NameValueCollection properties)
        {
        }


        private void MoveWorkItem(WorkItem workItem, WorkStep toStep)
        {
            WorkStep stepToMoveTo = toStep;

            WorkItem workItemToMove = workItem;

            if (_workItemQuery.IsParallelLockedWorkItem(workItem))
            {
                throw new InvalidOperationException("Work item is locked for parallel work");
            }

            if (_workItemQuery.IsExpandLocked(workItem))
            {
                throw new InvalidOperationException("Item is expandlocked and cannot be moved");
            }

            WorkStep parallelStep;
            if (_workStepQuery.IsWithinParallelStep(toStep, out parallelStep))
            {
                if (!_workItemQuery.IsChildOfParallelledWorkItem(workItem))
                {
                    string idToMove = ParallelStepHelper.GetParallelId(workItem.Id, parallelStep, toStep);
                    workItemToMove = MoveAndLockAndSplitForParallelism(workItem, parallelStep).First(wi => wi.Id==idToMove);
                }
                else
                {
                    workItemToMove = workItem; 
                }
            }

            if (_workStepQuery.IsExpandStep(toStep))
            {
                stepToMoveTo = CreateTransientWorkSteps(workItemToMove, stepToMoveTo);
                workItemToMove = workItemToMove.AddClass(stepToMoveTo.WorkItemClass);
            }


            if (_workItemQuery.IsChildOfParallelledWorkItem(workItemToMove))
            {
                if (IsMergeable(workItemToMove, toStep))
                {
                    workItemToMove = MergeParallelWorkItems(workItemToMove);
                }
            }

            if (!_workStepQuery.IsValidWorkStepForWorkItem(workItemToMove, stepToMoveTo))
            {
                throw new InvalidOperationException("Invalid step for work item");
            }

            WorkStep transientStep;
            if (_workStepQuery.IsInTransientStep(workItem, out transientStep))
            {
                _workflowRepository.DeleteWorkStepsRecursively(transientStep);

                workItemToMove = workItemToMove.RemoveClass(transientStep.WorkItemClass);
            }

            WorkItem movedWorkItem = Move(workItemToMove, stepToMoveTo);

            if (_workItemQuery.IsChildOfExpandedWorkItem(movedWorkItem))
            {
                TryUpdatingExpandLockOnParent(movedWorkItem);
            }

        }

        private WorkItem Move(WorkItem workItemToMove, WorkStep stepToMoveTo)
        {
            int ordinal = _workItemQuery.GetNextOrdinal(workItemToMove);
            WorkItem movedWorkItem = workItemToMove.MoveTo(stepToMoveTo).UpdateOrdinal(ordinal);

            _workItemRepository.UpdateWorkItem(movedWorkItem);
            return movedWorkItem;
        }

        private void TryUpdatingExpandLockOnParent(WorkItem item)
        {
            WorkItem parent = _workItemRepository.GetWorkItem(item.ParentId);

            if (_workItemRepository.GetChildWorkItems(parent.Id).All(_workItemQuery.IsDone))
            {
                parent = parent.UpdateStatus(WorkItemStatus.Normal);
            }
            else
            {
                parent = parent.UpdateStatus(WorkItemStatus.ExpandLocked);
            }

            _workItemRepository.UpdateWorkItem(parent);
        }



        private WorkStep CreateTransientWorkSteps(WorkItem item, WorkStep expandStep)
        {
            Debug.Assert(expandStep.Type==WorkStepType.Expand);

            var transientRootPath = expandStep.Path+"/"+item.Id;

            CreateTransientWorkStepsRecursively(transientRootPath,expandStep, item.Id);

            var workItemClass = WorkItemClass.Combine(expandStep.WorkItemClass, item.Id);
            var transientWorkStep = new WorkStep(transientRootPath, expandStep.Path, expandStep.Ordinal, WorkStepType.Transient, workItemClass);
            _workflowRepository.CreateWorkStep(transientWorkStep);

            return transientWorkStep;
        }

        private void CreateTransientWorkStepsRecursively(string transientRootPath, WorkStep rootStep, string workItemId)
        {
            var subSteps = _workflowRepository.GetChildWorkSteps(rootStep.Path);
            foreach (var childStep in subSteps)
            {
                var offset = childStep.Path.Remove(0, rootStep.Path.Length);

                var childTransientPath = transientRootPath + offset;

                var workItemClass = WorkItemClass.Combine(childStep.WorkItemClass,workItemId);
                _workflowRepository.CreateWorkStep(new WorkStep(childTransientPath, transientRootPath, childStep.Ordinal, childStep.Type, workItemClass));

                CreateTransientWorkStepsRecursively(childTransientPath, childStep, workItemId);
            }
        }


        private WorkItem MergeParallelWorkItems(WorkItem item)
        {
            WorkItem unlockedParent = _workItemRepository.GetWorkItem(item.ParentId).UpdateStatus(WorkItemStatus.Normal);
            _workItemRepository.UpdateWorkItem(unlockedParent);

            foreach (WorkItem child in _workItemRepository.GetChildWorkItems(item.ParentId).ToList())
            {
                _workItemRepository.Delete(child);
            }

            return unlockedParent;
        }

        private bool IsMergeable(WorkItem item, WorkStep toStep)
        {
            bool isMergeable = true;
            foreach (WorkItem child in _workItemRepository.GetChildWorkItems(item.ParentId).Where(wi=>wi.Id!=item.Id))
            {
                isMergeable &= child.Path == toStep.Path;
            }
            return isMergeable;
        }


        private IEnumerable<WorkItem> MoveAndLockAndSplitForParallelism(WorkItem item, WorkStep parallelRootStep)
        {
            var lockedAndMovedItem = item.MoveTo(parallelRootStep).UpdateStatus(WorkItemStatus.ParallelLocked);
            _workItemRepository.UpdateWorkItem(lockedAndMovedItem);

            var helper = new ParallelStepHelper(_workflowRepository);

            var splitWorkItems = helper.SplitForParallelism(item, parallelRootStep);

            foreach (var splitWorkItem in splitWorkItems)
            {
                _workItemRepository.CreateWorkItem(splitWorkItem);
            }

            return splitWorkItems;
        }


    }
}