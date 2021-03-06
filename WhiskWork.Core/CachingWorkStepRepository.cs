﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace WhiskWork.Core
{
    public class CachingWorkStepRepository : IWorkStepRepository
    {
        private readonly Dictionary<string, WorkStep> _workSteps;
        private readonly ICacheableWorkStepRepository _innerRepository;

        public CachingWorkStepRepository(ICacheableWorkStepRepository innerRepository)
        {
            _innerRepository = innerRepository;
            _workSteps = LoadWorkSteps();
        }

        private Dictionary<string, WorkStep> LoadWorkSteps()
        { 
            var workSteps = new Dictionary<string, WorkStep>();

            foreach (var workStep in _innerRepository.GetAllWorkSteps())
            {
                workSteps.Add(workStep.Path,workStep);
            }
            
            return workSteps;
        }

        public void CreateWorkStep(WorkStep workStep)
        {
            _innerRepository.CreateWorkStep(workStep);

            _workSteps.Add(workStep.Path, workStep);
        }

        public IEnumerable<WorkStep> GetChildWorkSteps(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException();
            }

            return _workSteps.Values.Where(step => step.ParentPath == path).ToList();
        }

        public WorkStep GetWorkStep(string path)
        {
            ThrowIfNotExists(path);

            return _workSteps[path];
        }

        public void DeleteWorkStep(string path)
        {
            _innerRepository.DeleteWorkStep(path);

            _workSteps.Remove(path);
        }

        public void UpdateWorkStep(WorkStep workStep)
        {
            ThrowIfNotExists(workStep.Path);

            _innerRepository.UpdateWorkStep(workStep);

            _workSteps[workStep.Path] = workStep;
        }

        public bool ExistsWorkStep(string path)
        {
            return _workSteps.ContainsKey(path);
        }

        private void ThrowIfNotExists(string path)
        {
            if (!_workSteps.ContainsKey(path))
            {
                throw new ArgumentException("Workstep not found: '" + path + "'");
            }
        }


    }
}