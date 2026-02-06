using System;
using System.Collections.Generic;

namespace MSGS
{
    public class GoToEngine
    {
        private int Steps = 480; // Default minimum steps
        private const int DOF = 7;
        private float _speed_limit;

        public List<byte[]> Generate(
            ref RKS2RC_Control rcCmd, 
            RC2RKS_Status rcStatus,
            bool acivateEngines,
            int speed_limit)
        {
            _speed_limit = (float)speed_limit;

            if (acivateEngines)
            {
                //enable all engines
                for (int i = 0; i < (int)(int)eRcSubsystems.eRcNumOfSubsystems; i++)
                {
                    rcCmd.subsystem_cmd[i] = eSysState.eActive;
                }
            }

            CalculateSteps(ref rcCmd, ref rcStatus);
            Steps = Steps > 5000 ? 5000 : Steps; // Cap steps to prevent overflow
            Console.WriteLine($"GoToEngine: Steps = {Steps}");

            var data = new List<byte[]>();

            var leftDiffs = GenerateEngineDiffs(rcCmd.manipulator_cmd_left, rcStatus.manipulator_status[0]);
            var rightDiffs = GenerateEngineDiffs(rcCmd.manipulator_cmd_right, rcStatus.manipulator_status[1]);

            for (int line = 0; line < Steps; line++)
            {
                ApplyLineDiffs(rcCmd, leftDiffs, rightDiffs, line);
                data.Add(MSGHelper.StructureToByteArray(rcCmd));
            }

            //add half a second last command for stability
            for (int i = 0; i < 240; i++)
            {
                data.Add(MSGHelper.StructureToByteArray(rcCmd));
            }

            return data;
        }

        private void CalculateSteps(ref RKS2RC_Control rcCmd, ref RC2RKS_Status rcStatus)
        {
            Steps = 480; // Minimum.
            CalculateManipulatorSteps(
                rcCmd.manipulator_cmd_left.target.poseArr,
                rcStatus.manipulator_status[0].pose.poseArr);

            CalculateManipulatorSteps(
                rcCmd.manipulator_cmd_right.target.poseArr,
                rcStatus.manipulator_status[1].pose.poseArr);
        }

        private void CalculateManipulatorSteps(float[] targetPose, float[] currentPose)
        {
            Console.WriteLine($"Calculating speed limit for goto engines. Speed limit: {_speed_limit}");
            for (int i = 0; i < DOF; i++)
            {
                float diff = Math.Abs(targetPose[i] - currentPose[i]);
                float current_limit = i < 6 ? _speed_limit : 20.0f; //for M7 - Const 20
                float current_num_of_steps = (diff / current_limit) * 480.0f;
                Steps = (int)Math.Max(Steps, current_num_of_steps);
                if(current_num_of_steps > 3000)
                {
                    Console.WriteLine($"Warning: Goto Engine very big step {current_num_of_steps}! Engine: {i}. target: {targetPose[i]}. current: {currentPose[i]}");
                }
            }
        }

        private List<List<double>> GenerateEngineDiffs(
            sRcManipulatorCmd cmd,
            sRcManipulatorStatus status)
        {
            var diffs = new List<List<double>>(DOF);

            for (int i = 0; i < DOF; i++)
            {
                double start = status.pose.poseArr[i];
                double end = cmd.target.poseArr[i];
                double stepSize = (end - start) / Steps;

                var jointSteps = new List<double>(Steps);
                for (int step = 0; step < Steps; step++)
                {
                    jointSteps.Add(start + stepSize * step);
                }

                diffs.Add(jointSteps);
            }

            return diffs;
        }


        private void ApplyLineDiffs(
            RKS2RC_Control rcCmd, 
            List<List<double>> leftDiffs,
            List<List<double>> rightDiffs,
            int line)
        {
            for (int i = 0; i < DOF; i++)
            {
                ReflectionHelper.SetNestedPropertyValue(ref rcCmd, $"manipulator_cmd_left.target.poseArr[{i}]", leftDiffs[i][line]);
                ReflectionHelper.SetNestedPropertyValue(ref rcCmd, $"manipulator_cmd_right.target.poseArr[{i}]", rightDiffs[i][line]);
            }
        }
    }
}
