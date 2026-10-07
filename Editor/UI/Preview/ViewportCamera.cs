using System.Text.Json.Nodes;
using Editor;
using Sandbox;
using WeaponImporter.EditorTools.Core.Analysis;
using WeaponImporter.EditorTools.Core.Setup;
using N = System.Numerics;

namespace WeaponImporter.EditorTools.UI.Preview;
using WeaponImporter.Components;
using WeaponImporter.EditorTools.Engine;

public enum ViewportCamera { Orbit, Gameplay, Front, Side, FirstPerson }
