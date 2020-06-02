using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;

namespace Hoverbike
{
    public class BikeAgentHeuristicOnly : Agent
    {
        private Bike bike;
        private bool isDiscrete;
  
        public override void Initialize()
        {
            isDiscrete = GetComponent<BehaviorParameters>()
                .BrainParameters.VectorActionSpaceType == SpaceType.Discrete;

            bike = GetComponentInChildren<Bike>();
            bike.Initialize();
        }

        public override void OnActionReceived(float[] vectorAction)
        {
            if (isDiscrete)
            {
                bike.OnAgentAction(vectorAction, Time.fixedDeltaTime, 0);
            }
            else
            {
                bike.OnAgentAction(vectorAction, 0);
            }
        }

        public override void Heuristic(float[] actionsOut)
        {
            float v = Input.GetAxis("Vertical");
            float h = Input.GetAxis("Horizontal");

            if (isDiscrete)
            {
                actionsOut[0] = 1;
                actionsOut[1] = 1;

                if (Mathf.Abs(v) > Mathf.Epsilon)
                {
                    actionsOut[0] += Mathf.Sign(v);
                }

                if (Mathf.Abs(h) > Mathf.Epsilon)
                {
                    actionsOut[1] += Mathf.Sign(h);
                }
            }
            else
            {
                actionsOut[0] = v;
                actionsOut[1] = h;
            }
        }
    }
}