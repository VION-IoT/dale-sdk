# Vion.Examples.ModbusRtu

A Vion IoT Library template for creating LogicBlocks.

## Getting Started

1. **Set the startup project:**
   - **Visual Studio:** Right-click `Vion.Examples.ModbusRtu.DevHost` in Solution Explorer → **"Set as Startup Project"**
   - **Rider:** Select `Vion.Examples.ModbusRtu.DevHost` from the run configuration dropdown (top-right toolbar)

2. **Run the DevHost:**
   - Press `F5` to run
   - The browser should open automatically at `http://localhost:5000`

3. **Develop your LogicBlocks:**
   - Add your LogicBlock implementations in the `Vion.Examples.ModbusRtu` project
   - Register them in `DependencyInjection.cs`
   - Configure them in `Vion.Examples.ModbusRtu.DevHost/Program.cs`