using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Clarion;
using Clarion.Framework;
using Clarion.Framework.Core;
using Clarion.Framework.Templates;
using ClarionApp.Model;
using ClarionApp;
using System.Threading;
using Gtk;

namespace ClarionApp
{
	/// <summary>
	/// Public enum that represents all possibilities of agent actions
	/// </summary>
	public enum CreatureActions
	{
		DO_NOTHING,
		ROTATE_CLOCKWISE,
		GO_AHEAD,
		MOVE_TO_FOOD,
		GET_FOOD,
		MOVE_TO_JEWEL,
		GET_JEWEL,
		MOVE_DELIVERY_SPOT,
		DELIVER_LEAFLET
	}

	public class ClarionAgent
	{
		#region Constants
		/// <summary>
		/// Constant that represents the Visual Sensor
		/// </summary>
		private String SENSOR_VISUAL_DIMENSION = "VisualSensor";

		private String MEMORY = "Memory";

		private String INTERNAL_SENSOR = "InternalSensor";
		/// <summary>
		/// Constant that represents that there is at least one wall ahead
		/// </summary>
		private String DIMENSION_WALL_AHEAD = "WallAhead";
		double prad = 0;
		#endregion

		#region Properties
		public MindViewer mind;
		String creatureId = String.Empty;
		String creatureName = String.Empty;
		#region Simulation
		/// <summary>
		/// If this value is greater than zero, the agent will have a finite number of cognitive cycle. Otherwise, it will have infinite cycles.
		/// </summary>
		public double MaxNumberOfCognitiveCycles = -1;
		/// <summary>
		/// Current cognitive cycle number
		/// </summary>
		private double CurrentCognitiveCycle = 0;
		/// <summary>
		/// Time between cognitive cycle in miliseconds
		/// </summary>
		public Int32 TimeBetweenCognitiveCycles = 50;
		/// <summary>
		/// A thread Class that will handle the simulation process
		/// </summary>
		private Thread runThread;

		private bool processingCommand = false;
		#endregion

		#region Agent
		private WSProxy worldServer;
		/// <summary>
		/// The agent 
		/// </summary>
		private Clarion.Framework.Agent CurrentAgent;
		#endregion

		#region Perception Input
		/// <summary>
		/// Perception input to indicates a wall ahead
		/// </summary>
		private DimensionValuePair inputWallAhead;
		private DimensionValuePair inputHasFoodInMemory;
		private DimensionValuePair inputHasJewelInMemory;
		private DimensionValuePair inputLowFuel;
		private DimensionValuePair inputFoodAhead;
		private DimensionValuePair inputJewelAhead;
		private DimensionValuePair inputCanCompleteLeaflet;
		private DimensionValuePair inputDeliverySpotAhead;
		#endregion

		#region Action Output
		/// <summary>
		/// Output action that makes the agent to rotate clockwise
		/// </summary>
		private ExternalActionChunk outputRotateClockwise;
		/// <summary>
		/// Output action that makes the agent go ahead
		/// </summary>
		private ExternalActionChunk outputGoAhead;

		private ExternalActionChunk outputMoveToFood;
		private ExternalActionChunk outputMoveToJewel;

		private ExternalActionChunk outputGetFood;
		private ExternalActionChunk outputGetJewel;

		private ExternalActionChunk outputMoveToDeliverySpot;
		private ExternalActionChunk outputDeliverLeaflet;
		#endregion

		#endregion

		#region Variaveis
		IList<Thing> memoryJewel = new List<Thing> ();
		IList<Thing> memoryFood = new List<Thing> ();

		HashSet<string> gotFood = new HashSet<string> ();
		HashSet<string> gotJewel = new HashSet<string> ();

		private IList<String> colors = new List<string> () { "Red", "Yellow", "White", "Green", "Blue", "Magenta" };

		bool canCompleteLeaflet = false;

		private Dictionary<string, int> getColorInt = new Dictionary<string, int> {
			{"Red", 0},
			{"Green", 1},
			{"Blue", 2},
			{"Yellow", 3},
			{"Magenta", 4},
			{"White", 5}
		  };

		Creature creature;
		#endregion

		#region Constructor
		public ClarionAgent (WSProxy nws, String creature_ID, String creature_Name)
		{
			worldServer = nws;
			// Initialize the agent
			CurrentAgent = World.NewAgent ("Current Agent");
			mind = new MindViewer ();
			mind.Show ();
			creatureId = creature_ID;
			creatureName = creature_Name;

			IList<Thing> currentSceneInWS3D = processSensoryInformation ();
			creature = (Creature)currentSceneInWS3D.Where (item => (item.CategoryId == Thing.CATEGORY_CREATURE)).First ();

			var leaflets = creature.getLeaflets ();
			var leaflet = creature.getLeaflets ().ElementAt (0);

			int initialDistX = 600;
			foreach (var item in colors){
				int required = leaflet.getRequired (item);

				for(int i = 0; i<required; i++) {
					nws.NewJewel (getColorInt [item], initialDistX, 200);
					initialDistX += 20;
				}
			}

			// Initialize Input Information
			inputWallAhead = World.NewDimensionValuePair (SENSOR_VISUAL_DIMENSION, DIMENSION_WALL_AHEAD);
			inputHasFoodInMemory = World.NewDimensionValuePair (MEMORY, "HasFoodInMemory");
			inputHasJewelInMemory = World.NewDimensionValuePair (MEMORY, "HasJewelInMemory");
			inputCanCompleteLeaflet = World.NewDimensionValuePair (MEMORY, "CanCompleteLeaflet");
			inputLowFuel = World.NewDimensionValuePair (INTERNAL_SENSOR, "LowFuel");
			inputFoodAhead = World.NewDimensionValuePair (SENSOR_VISUAL_DIMENSION, "FoodAhead");
			inputJewelAhead = World.NewDimensionValuePair (SENSOR_VISUAL_DIMENSION, "JewelAhead");
			inputDeliverySpotAhead = World.NewDimensionValuePair (SENSOR_VISUAL_DIMENSION, "DeliverySpotAhead");

			// Initialize Output actions
			outputRotateClockwise = World.NewExternalActionChunk (CreatureActions.ROTATE_CLOCKWISE.ToString ());
			outputGoAhead = World.NewExternalActionChunk (CreatureActions.GO_AHEAD.ToString ());
			outputMoveToFood = World.NewExternalActionChunk (CreatureActions.MOVE_TO_FOOD.ToString ());
			outputMoveToJewel = World.NewExternalActionChunk (CreatureActions.MOVE_TO_JEWEL.ToString ());
			outputMoveToDeliverySpot = World.NewExternalActionChunk (CreatureActions.MOVE_DELIVERY_SPOT.ToString ());
			outputGetFood = World.NewExternalActionChunk (CreatureActions.GET_FOOD.ToString ());
			outputGetJewel = World.NewExternalActionChunk (CreatureActions.GET_JEWEL.ToString ());
			outputDeliverLeaflet = World.NewExternalActionChunk (CreatureActions.DELIVER_LEAFLET.ToString ());



			//Create thread to simulation
			runThread = new Thread (CognitiveCycle);
			Console.WriteLine ("Agent started");
		}
		#endregion

		#region Public Methods
		/// <summary>
		/// Run the Simulation in World Server 3d Environment
		/// </summary>
		public void Run ()
		{
			Console.WriteLine ("Running ...");
			// Setup Agent to run
			if (runThread != null && !runThread.IsAlive) {
				SetupAgentInfraStructure ();
				// Start Simulation Thread                
				runThread.Start (null);
			}
		}

		/// <summary>
		/// Abort the current Simulation
		/// </summary>
		/// <param name="deleteAgent">If true beyond abort the current simulation it will die the agent.</param>
		public void Abort (Boolean deleteAgent)
		{
			Console.WriteLine ("Aborting ...");
			if (runThread != null && runThread.IsAlive) {
				runThread.Abort ();
			}

			if (CurrentAgent != null && deleteAgent) {
				CurrentAgent.Die ();
			}
		}

		IList<Thing> processSensoryInformation ()
		{
			IList<Thing> response = null;

			if (worldServer != null && worldServer.IsConnected) {
				response = worldServer.SendGetCreatureState (creatureName);
				prad = (Math.PI / 180) * response.First ().Pitch;
				while (prad > Math.PI) prad -= 2 * Math.PI;
				while (prad < -Math.PI) prad += 2 * Math.PI;
				Sack s = worldServer.SendGetSack ("0");
				mind.setBag (s);
			}

			return response;
		}

		void processSelectedAction (CreatureActions externalAction)
		{
			Thread.CurrentThread.CurrentCulture = new CultureInfo ("en-US");
			if (worldServer != null && worldServer.IsConnected) {
				Console.WriteLine ("ação: " + externalAction.ToString ());
				switch (externalAction) {
				case CreatureActions.DO_NOTHING:
					// Do nothing as the own value says
					break;
				case CreatureActions.ROTATE_CLOCKWISE:
					worldServer.SendSetAngle (creatureId, 2, -2, 2);
					break;
				case CreatureActions.GO_AHEAD:
					worldServer.SendSetAngle (creatureId, 1, 1, prad);
					break;
				case CreatureActions.MOVE_TO_FOOD:

					Thing thingMoveFood = getNearestFood ();
					while (true) {
						processingCommand = true;
						Thread.Sleep (100);

						var listThings = worldServer.SendGetCreatureState (creatureName);
						if (!listThings.Any (item => (item.Name == thingMoveFood.Name))) {
							break;
						}

						thingMoveFood = listThings.Where (item => (item.Name == thingMoveFood.Name)).First ();

						if (thingMoveFood.DistanceToCreature <= 15) {
							break;
						}
						worldServer.SendMoveTo (creatureId, 1, 1, thingMoveFood.X1, thingMoveFood.Y1); // ou a direção da joia
					}
					processingCommand = false;
					break;

				case CreatureActions.MOVE_TO_JEWEL:
					Thing thingMoveJewel = getNearestJewel ();
					while (true) {
						processingCommand = true;
						Thread.Sleep (100);

						var listThings  = worldServer.SendGetCreatureState (creatureName);
						if (!listThings.Any (item => (item.Name == thingMoveJewel.Name))) {
							break;
						}

						thingMoveJewel = listThings.Where (item => (item.Name == thingMoveJewel.Name)).First ();

						if (thingMoveJewel.DistanceToCreature <= 15) {
							break;
						}
						worldServer.SendMoveTo (creatureId, 1, 1, thingMoveJewel.X1, thingMoveJewel.Y1); // ou a direção da joia
					}
					processingCommand = false;
					break;

				case CreatureActions.GET_FOOD:
					Thing thingGetFood = getNearestFood ();
					if (thingGetFood == null) {
						break;
					}
					Thing itemToRemove = null;
					foreach (Thing item in memoryFood) {
						if (item.Name == thingGetFood.Name) {
							itemToRemove = item;
							break; // Encontrou o primeiro, pode parar
						}
					}

					if (itemToRemove != null) {
						memoryFood.Remove (itemToRemove);
					} // Remove primeiro para evitar reentrada
					gotFood.Add (thingGetFood.Name);       // Marca como coletado
					worldServer.SendEatIt (creatureId, thingGetFood.Name);
					break;

				case CreatureActions.GET_JEWEL:
					Thing thingGetJewel = getNearestJewel ();
					if (thingGetJewel == null) {
						break;
					}
					worldServer.SendSackIt (creatureId, thingGetJewel.Name); // ou a direção da joia
					gotJewel.Add (thingGetJewel.Name);
					Thing itemToRemoveb = null;
					foreach (Thing item in memoryJewel) {
						if (item.Name == thingGetJewel.Name) {
							itemToRemoveb = item;
							break; // Encontrou o primeiro, pode parar
						}
					}

					if (itemToRemoveb != null) {
						memoryJewel.Remove (itemToRemoveb);
					}

					break;

				case CreatureActions.MOVE_DELIVERY_SPOT:
					Thing thingDeliverySpot;
					while (true) {
						processingCommand = true;
						Thread.Sleep (100);

						var listThings = worldServer.SendGetCreatureState (creatureName);
						if (!listThings.Any (item => (item.CategoryId== Thing.CATEGORY_DeliverySPOT))) {
							break;
						}

						thingDeliverySpot = listThings.Where (item => (item.CategoryId == Thing.CATEGORY_DeliverySPOT)).First ();

						if (thingDeliverySpot.DistanceToCreature <= 50) {
							break;
						}
						worldServer.SendMoveTo (creatureId, 1, 1, 300, 300); // ou a direção da joia
					}
					processingCommand = false;
					break;

				case CreatureActions.DELIVER_LEAFLET:
					foreach(var item in creature.getLeaflets ()) {
						creature = getCreatureInstance ();
						if (item.canCompleteLeflet ()) {
							worldServer.DeliverLeaflet (creatureId, Convert.ToInt32(item.leafletID));
						}
					}
					break;
				default:
					break;
				}
			}
		}

		private Creature getCreatureInstance ()
		{
			IList<Thing> currentSceneInWS3D = processSensoryInformation ();
			return  (Creature)currentSceneInWS3D.Where (item => (item.CategoryId == Thing.CATEGORY_CREATURE)).First ();
		}

		private Thing getNearestFood ()
		{
			Thing nearest = null;
			double minDistance = double.MaxValue;

			foreach (Thing food in memoryFood) {
				double distance = getDistance (food.X1, food.Y1, creature.X1, creature.Y1);

				if (distance < minDistance) {
					minDistance = distance;
					nearest = food;
				}
			}

			return nearest;
		}

		private Thing getNearestJewel ()
		{
			Thing nearest = null;
			double minDistance = double.MaxValue;

			foreach (Thing jewel in memoryJewel) {
				double distance = getDistance (jewel.X1, jewel.Y1, creature.X1, creature.Y1);

				if (distance < minDistance) {
					minDistance = distance;
					nearest = jewel;
				}
			}

			return nearest;
		}

		private double getDistance (double xThing, double yThing, double xCreature, double yCreature)
		{
			double dx = xCreature - xThing;
			double dy = yCreature - yThing;
			return Math.Sqrt (dx * dx + dy * dy);
		}

		#endregion

		#region Setup Agent Methods
		/// <summary>
		/// Setup agent infra structure (ACS, NACS, MS and MCS)
		/// </summary>
		private void SetupAgentInfraStructure ()
		{
			// Setup the ACS Subsystem
			SetupACS ();
		}

		private void SetupMS ()
		{
			//RichDrive
		}

		/// <summary>
		/// Setup the ACS subsystem
		/// </summary>
		private void SetupACS ()
		{
			// Create Rule to avoid collision with wall
			SupportCalculator avoidCollisionWallSupportCalculator = FixedRuleToAvoidCollisionWall;
			FixedRule ruleAvoidCollisionWall = AgentInitializer.InitializeActionRule (CurrentAgent, FixedRule.Factory, outputRotateClockwise, avoidCollisionWallSupportCalculator);

			// Commit this rule to Agent (in the ACS)
			CurrentAgent.Commit (ruleAvoidCollisionWall);

			// Create Colission To Go Ahead
			SupportCalculator goAheadSupportCalculator = FixedRuleToGoAhead;
			FixedRule ruleGoAhead = AgentInitializer.InitializeActionRule (CurrentAgent, FixedRule.Factory, outputGoAhead, goAheadSupportCalculator);

			// Commit this rule to Agent (in the ACS)
			CurrentAgent.Commit (ruleGoAhead);

			SupportCalculator moveToFoodSupport = FixedRuleToMoveToFood;
			FixedRule ruleMoveToFood = AgentInitializer.InitializeActionRule (CurrentAgent, FixedRule.Factory, outputMoveToFood, moveToFoodSupport);
			CurrentAgent.Commit (ruleMoveToFood);

			SupportCalculator moveToJewelSupport = FixedRuleToMoveToJewel;
			FixedRule ruleMoveToJewel = AgentInitializer.InitializeActionRule (CurrentAgent, FixedRule.Factory, outputMoveToJewel, moveToJewelSupport);
			CurrentAgent.Commit (ruleMoveToJewel);

			SupportCalculator getFoodSupport = FixedRuleToGetFood;
			FixedRule ruleGetFood = AgentInitializer.InitializeActionRule (CurrentAgent, FixedRule.Factory, outputGetFood, getFoodSupport);
			CurrentAgent.Commit (ruleGetFood);

			SupportCalculator getJewelSupport = FixedRuleToGetJewel;
			FixedRule ruleGetJewel = AgentInitializer.InitializeActionRule (CurrentAgent, FixedRule.Factory, outputGetJewel, getJewelSupport);
			CurrentAgent.Commit (ruleGetJewel);

			SupportCalculator moveToDeliverySpotSupport = FixedRuleToMoveToDeliverySpot;
			FixedRule ruleMoveToDeliverySpot = AgentInitializer.InitializeActionRule (CurrentAgent, FixedRule.Factory, outputMoveToDeliverySpot, moveToDeliverySpotSupport);
			CurrentAgent.Commit (ruleMoveToDeliverySpot);

			SupportCalculator deliverLeafletSupport = FixedRuleToDeliverLeaflet;
			FixedRule ruleDeliverLeaflet = AgentInitializer.InitializeActionRule (CurrentAgent, FixedRule.Factory, outputDeliverLeaflet, deliverLeafletSupport);
			CurrentAgent.Commit (ruleDeliverLeaflet);

			// Disable Rule Refinement
			CurrentAgent.ACS.Parameters.PERFORM_RER_REFINEMENT = false;

			// The selection type will be probabilistic
			CurrentAgent.ACS.Parameters.LEVEL_SELECTION_METHOD = ActionCenteredSubsystem.LevelSelectionMethods.STOCHASTIC;

			// The action selection will be fixed (not variable) i.e. only the statement defined above.
			CurrentAgent.ACS.Parameters.LEVEL_SELECTION_OPTION = ActionCenteredSubsystem.LevelSelectionOptions.FIXED;

			// Define Probabilistic values
			CurrentAgent.ACS.Parameters.FIXED_FR_LEVEL_SELECTION_MEASURE = 1;
			CurrentAgent.ACS.Parameters.FIXED_IRL_LEVEL_SELECTION_MEASURE = 0;
			CurrentAgent.ACS.Parameters.FIXED_BL_LEVEL_SELECTION_MEASURE = 0;
			CurrentAgent.ACS.Parameters.FIXED_RER_LEVEL_SELECTION_MEASURE = 0;
		}

		/// <summary>
		/// Make the agent perception. In other words, translate the information that came from sensors to a new type that the agent can understand
		/// </summary>
		/// <param name="sensorialInformation">The information that came from server</param>
		/// <returns>The perceived information</returns>
		private SensoryInformation prepareSensoryInformation (IList<Thing> listOfThings)
		{
			// New sensory information
			SensoryInformation si = World.NewSensoryInformation (CurrentAgent);

			// Detect if we have a wall ahead
			Boolean wallAhead = listOfThings.Where (item => (item.CategoryId == Thing.CATEGORY_BRICK && item.DistanceToCreature <= 61)).Any ();
			double wallAheadActivationValue = wallAhead ? CurrentAgent.Parameters.MAX_ACTIVATION : CurrentAgent.Parameters.MIN_ACTIVATION;
			si.Add (inputWallAhead, wallAheadActivationValue);


			//Console.WriteLine(sensorialInformation);
			creature = (Creature)listOfThings.Where (item => (item.CategoryId == Thing.CATEGORY_CREATURE)).First ();
			canCompleteLeaflet = seeIfCanCompleteAnyLeaflet ();

			if (!canCompleteLeaflet) {
				double foodMemoryActivation = (memoryFood.Count () > 0) ? CurrentAgent.Parameters.MAX_ACTIVATION : CurrentAgent.Parameters.MIN_ACTIVATION;
				si.Add (inputHasFoodInMemory, foodMemoryActivation);

				// Percepção: combustível baixo
				double lowFuelActivation = (creature.Fuel < 1000) ? CurrentAgent.Parameters.MAX_ACTIVATION : CurrentAgent.Parameters.MIN_ACTIVATION;
				si.Add (inputLowFuel, lowFuelActivation);

				Boolean foodAhead = listOfThings.Where (item => ((item.CategoryId == Thing.CATEGORY_FOOD || item.CategoryId == Thing.categoryPFOOD || item.CategoryId == Thing.CATEGORY_NPFOOD) && item.DistanceToCreature <= 15)).Any ();
				if (foodAhead) {
					Console.Write ("food ahead true");
				}

				double foodAheadActivationValue = foodAhead ? CurrentAgent.Parameters.MAX_ACTIVATION : CurrentAgent.Parameters.MIN_ACTIVATION;
				si.Add (inputFoodAhead, foodAheadActivationValue);


				double jewelInMemoryActivationValue = memoryJewel.Any() ? CurrentAgent.Parameters.MAX_ACTIVATION : CurrentAgent.Parameters.MIN_ACTIVATION;
				si.Add (inputHasJewelInMemory, jewelInMemoryActivationValue);

				Boolean jewelAhead = listOfThings.Where (item => ((item.CategoryId == Thing.CATEGORY_JEWEL) && item.DistanceToCreature <= 15)).Any ();
				if (foodAhead) {
					Console.Write ("jewel ahead true");
				}

				double jewelAheadActivationValue = jewelAhead ? CurrentAgent.Parameters.MAX_ACTIVATION : CurrentAgent.Parameters.MIN_ACTIVATION;
				si.Add (inputJewelAhead, jewelAheadActivationValue);

				si.Add (inputCanCompleteLeaflet, CurrentAgent.Parameters.MIN_ACTIVATION);

				Boolean deliverySpotAhead = listOfThings.Where (item => ((item.CategoryId == Thing.CATEGORY_DeliverySPOT) && item.DistanceToCreature <= 50)).Any ();
				if (deliverySpotAhead) {
					Console.Write ("jewel ahead true");
				}

				double deliverySpotAheadActivationValue = deliverySpotAhead ? CurrentAgent.Parameters.MAX_ACTIVATION : CurrentAgent.Parameters.MIN_ACTIVATION;
				si.Add (inputDeliverySpotAhead, deliverySpotAheadActivationValue);
			} else {
				si.Add (inputCanCompleteLeaflet, CurrentAgent.Parameters.MAX_ACTIVATION);
			}

			int n = 0;
			foreach (Leaflet l in creature.getLeaflets ()) {
				mind.updateLeaflet (n, l);
				n++;
			}
			return si;
		}

		private bool seeIfCanCompleteAnyLeaflet ()
		{
			bool canComplete = false;
			var leaflets = creature.getLeaflets ();
			foreach(var item in leaflets) {
				if (item.canCompleteLeflet ()) {
					canComplete = true;
				}
			}

			return canComplete;
		}
		#endregion

		#region Fixed Rules
		private double FixedRuleToAvoidCollisionWall (ActivationCollection currentInput, Rule target)
		{
			// See partial match threshold to verify what are the rules available for action selection
			return ((currentInput.Contains (inputWallAhead, CurrentAgent.Parameters.MAX_ACTIVATION))) ? 1 : 0.0;
		}

		private double FixedRuleToGoAhead (ActivationCollection currentInput, Rule target)
		{
			// Here we will make the logic to go ahead
			return ((currentInput.Contains (inputWallAhead, CurrentAgent.Parameters.MIN_ACTIVATION))) ? 1 : 0.0;
		}

		private double FixedRuleToMoveToFood (ActivationCollection currentInput, Rule target)
		{
			return (
				currentInput.Contains (inputHasFoodInMemory, CurrentAgent.Parameters.MAX_ACTIVATION) &&
				currentInput.Contains (inputLowFuel, CurrentAgent.Parameters.MAX_ACTIVATION)
			) ? 1.0 : 0.0;
		}

		private double FixedRuleToMoveToJewel (ActivationCollection currentInput, Rule target)
		{
			return (currentInput.Contains (inputHasJewelInMemory, CurrentAgent.Parameters.MAX_ACTIVATION)) ? 1.0 : 0.0;
		}

		private double FixedRuleToGetFood (ActivationCollection currentInput, Rule target)
		{
			return (currentInput.Contains (inputFoodAhead, CurrentAgent.Parameters.MAX_ACTIVATION)) ? 3.0 : 0.0;
		}

		private double FixedRuleToGetJewel (ActivationCollection currentInput, Rule target)
		{
			return (currentInput.Contains (inputJewelAhead, CurrentAgent.Parameters.MAX_ACTIVATION)) ? 2.0 : 0.0;
		}

		private double FixedRuleToMoveToDeliverySpot(ActivationCollection currentInput, Rule target)
		{
			return (currentInput.Contains (inputCanCompleteLeaflet, CurrentAgent.Parameters.MAX_ACTIVATION)) ? 5.0 : 0.0;
		}

		private double FixedRuleToDeliverLeaflet (ActivationCollection currentInput, Rule target)
		{
			return (currentInput.Contains (inputDeliverySpotAhead, CurrentAgent.Parameters.MAX_ACTIVATION)) ? 10.0 : 0.0;
		}
		#endregion

		#region Run Thread Method
		private void CognitiveCycle (object obj)
		{

			Console.WriteLine ("Starting Cognitive Cycle ... press CTRL-C to finish !");
			// Cognitive Cycle starts here getting sensorial information
			while (CurrentCognitiveCycle != MaxNumberOfCognitiveCycles) {
				if (!processingCommand) {


					// Get current sensory information                    
					IList<Thing> currentSceneInWS3D = processSensoryInformation ();

					setupJewelAndFoodList (currentSceneInWS3D);

					// Make the perception
					SensoryInformation si = prepareSensoryInformation (currentSceneInWS3D);

					//Perceive the sensory information
					CurrentAgent.Perceive (si);

					//Choose an action
					ExternalActionChunk chosen = CurrentAgent.GetChosenExternalAction (si);

					// Get the selected action
					String actionLabel = chosen.LabelAsIComparable.ToString ();
					CreatureActions actionType = (CreatureActions)Enum.Parse (typeof (CreatureActions), actionLabel, true);

					// Call the output event handler
					processSelectedAction (actionType);

					// Increment the number of cognitive cycles
					CurrentCognitiveCycle++;

					//Wait to the agent accomplish his job
					if (TimeBetweenCognitiveCycles > 0) {
						Thread.Sleep (TimeBetweenCognitiveCycles);
					}
				}
			}
		}


		private void setupJewelAndFoodList (IList<Thing> currentSceneInWS3D)
		{
			foreach (var thing in currentSceneInWS3D) {
				if (thing.CategoryId == 21 || thing.CategoryId == 2 || thing.CategoryId == 22) {
					// Verifica se já foi coletado OU se já está na memória
					if (!gotFood.Contains (thing.Name) && !memoryFood.Any (t => t.Name == thing.Name)) {
						memoryFood.Add (thing);
					}
				} else if (thing.CategoryId == 3) {
					if (!gotJewel.Contains (thing.Name) && !memoryJewel.Any (t => t.Name == thing.Name)) {
						memoryJewel.Add (thing);
					}
				}
			}
		}
		#endregion

	}
}
