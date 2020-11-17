using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DesignAutomationFramework;
using Newtonsoft.Json;

namespace StadiumPanels
{
   [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
   [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
   public class StadiumPanels : IExternalDBApplication
   {
      private static Random Rand = new Random();
      private static Dictionary<string, ElementId> NameToSymbolId = new Dictionary<string, ElementId>();


      public ExternalDBApplicationResult OnStartup(ControlledApplication app)
      {
         DesignAutomationBridge.DesignAutomationReadyEvent += HandleDesignAutomationReadyEvent;
         return ExternalDBApplicationResult.Succeeded;
      }

      public ExternalDBApplicationResult OnShutdown(ControlledApplication app)
      {
         return ExternalDBApplicationResult.Succeeded;
      }

      public void HandleDesignAutomationReadyEvent(object sender, DesignAutomationReadyEventArgs e)
      {
         GenerateStadiumPanels(e.DesignAutomationData);
         SaveAsProject(e.DesignAutomationData);
         e.Succeeded = true;
      }

      public int GetRandomInteger(int max) => Rand.Next(0, max);

      public double GetRandomNumber(double minimum, double maximum) => Rand.NextDouble() * (maximum - minimum) + minimum;

      FamilySymbolData GetWeigthedRandomFamilySymbolData(StadiumParams stadiumParams)
      {
         var totalWeight = stadiumParams.Data.Aggregate(0, (acc, data) => acc + data.Weight);
         var randomInt = GetRandomInteger(totalWeight);

         var weight = 0;
         for (var i = 0; i < stadiumParams.Data.Count; i++)
         {
            weight += stadiumParams.Data[i].Weight;
            if (randomInt < weight)
               return stadiumParams.Data[i];
         }

         return null;
      }

      public ElementId GetSymbolId(Document doc, string symbolName)
      {
         if (NameToSymbolId.ContainsKey(symbolName))
            return NameToSymbolId[symbolName];

         var familyName = symbolName;
         var symbolIds = new FilteredElementCollector(doc).OfClass(typeof(Family)).OfType<Family>().First(f => f.Name.Equals(familyName)).GetFamilySymbolIds();
         var symbol = symbolIds.Select(id => doc.GetElement(id)).OfType<FamilySymbol>().First(s => s.Name.Equals(symbolName));
         NameToSymbolId[symbolName] = symbol.Id;
         return NameToSymbolId[symbolName];
      }


      void RandomizeFamilyInstance(Document doc, FamilyInstance familyInstance, StadiumParams stadiumParams)
      {
         FamilySymbolData symbolData = GetWeigthedRandomFamilySymbolData(stadiumParams);
         ElementId familySymbolId = GetSymbolId(doc, symbolData.Name);

         familyInstance.ChangeTypeId(familySymbolId);
         foreach (var famParamData in symbolData.Params)
         {
            var param = familyInstance.ParametersMap.get_Item(famParamData.Name);
            var rand = GetRandomNumber(famParamData.Min, famParamData.Max);
            param.Set(rand);
            Console.WriteLine($"Family Instance {familyInstance.Id} has type {symbolData.Name} with parameter {famParamData.Name} value set to {rand}.");
         }
      }

      public void GenerateStadiumPanels(DesignAutomationData data)
      {
         var doc = data.RevitDoc;
         var surfaces = new FilteredElementCollector(doc).OfClass(typeof(DividedSurface)).ToElements();
         var rectangular = GetSymbolId(doc, "RectangularOpening");
         var stadiumParams = StadiumParams.Parse();

         using (Transaction transaction = new Transaction(doc))
         {
            transaction.Start("Create Components");

            foreach (var elem in surfaces)
            {
               var dividedSurface = elem as DividedSurface;
               dividedSurface.ChangeTypeId(rectangular);
               Console.WriteLine($"DividedSurface {dividedSurface.Id}: Changed type to {doc.GetElement(rectangular).Name}");
            }

            var instances = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).ToElements();

            for (var i=0; i<instances.Count; i++)
            {
               var inst = instances[i] as FamilyInstance;
               RandomizeFamilyInstance(doc, inst, stadiumParams);
            }

            transaction.Commit();
         }
      }

      public void SaveAsProject(DesignAutomationData data)
      {
         Document newDoc = data.RevitApp.NewProjectDocument(UnitSystem.Imperial);
         Family   family = data.RevitDoc.LoadFamily(newDoc);

         using (Transaction transaction = new Transaction(newDoc))
         {
            transaction.Start("Place Instance");
            family.Name = "Stadium";
            var symbol = newDoc.GetElement(family.GetFamilySymbolIds().Single()) as FamilySymbol;
            symbol.Name = "Stadium";
            symbol.Activate();
            newDoc.Create.NewFamilyInstance(XYZ.Zero, symbol, StructuralType.UnknownFraming);

            transaction.Commit();
         }

         newDoc.SaveAs("StadiumProject.rvt");
      }
   }

   class FamilyParamsData
   {
      [JsonProperty(PropertyName = "name")]
      public string Name { get; set; }

      [JsonProperty(PropertyName = "min")]
      public double Min { get; set; }

      [JsonProperty(PropertyName = "max")]
      public double Max { get; set; }
   }

   class FamilySymbolData
   {
      [JsonProperty(PropertyName = "name")]
      public string Name { get; set; }

      [JsonProperty(PropertyName = "weight")]
      public int Weight { get; set; }


      [JsonProperty(PropertyName = "params")]
      public IList<FamilyParamsData> Params { get; set; }
   }

   class StadiumParams
   {
      [JsonProperty(PropertyName = "data")]
      public IList<FamilySymbolData> Data { get; set; }

      static public StadiumParams Parse()
      {
         string jsonContents = File.ReadAllText("StadiumParams.json");
         return JsonConvert.DeserializeObject<StadiumParams>(jsonContents);
      }
   }
}
