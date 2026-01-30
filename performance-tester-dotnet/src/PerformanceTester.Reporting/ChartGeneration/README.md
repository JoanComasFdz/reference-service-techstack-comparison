# Chart Generation

Generates performance visualization charts using ScottPlot with 5 vertically-stacked subplots:

1. **Throughput** - Events/sec and API calls/sec with averages
2. **Service Resources** - CPU% and RAM (dual Y-axes) for the monitored process
3. **RabbitMQ Resources** - CPU% and RAM for the message broker container
4. **PostgreSQL Resources** - CPU% and RAM for the database container
5. **System Resources** - Overall system CPU% and RAM

## Architecture

The `ChartGenerator` orchestrates specialized components, each with a single responsibility:

```mermaid
classDiagram
    class ChartGenerator {
        -ChartConfig _config
        -ChartDataLoader _dataLoader
        -ThroughputPlotBuilder _throughputBuilder
        -ResourcePlotBuilder _resourceBuilder
        -PhaseOverlayRenderer _phaseRenderer
        -ChartImageComposer _imageComposer
        +GenerateChartAsync(outputPath, testReport)
    }

    class ChartConfig {
        <<record>>
        +FontConfig Font
        +LineConfig Line
        +PlotDimensions Dimensions
        +GridConfig Grid
    }

    class ChartDataLoader {
        +LoadThroughputReport(filePath)
        +LoadResourceReport(filePath)
        +LoadProcessResourceReport(filePath)
    }

    class ThroughputPlotBuilder {
        +Build(eventsData, apiData) Plot
    }

    class ResourcePlotBuilder {
        +Build(data, cpuLabel, ramLabel, colors) Plot
    }

    class PlotConfigurator {
        <<static>>
        +ConfigureTimeAxis(plot)
        +ConfigureGrid(plot, config)
        +ConfigureLegend(plot, fontConfig)
        +ApplyStandardConfiguration(plot, config)
    }

    class PhaseOverlayRenderer {
        +AddPhaseBoundaries(plot, testReport)
        +AddPhaseLabels(plot, testReport, yMax)
        +AddTitle(plot, testReport)
    }

    class ChartImageComposer {
        +RenderPlotToBitmap(plot, height) SKBitmap
        +CombineAndSave(bitmaps, outputPath)
    }

    ChartGenerator --> ChartConfig
    ChartGenerator --> ChartDataLoader
    ChartGenerator --> ThroughputPlotBuilder
    ChartGenerator --> ResourcePlotBuilder
    ChartGenerator --> PhaseOverlayRenderer
    ChartGenerator --> ChartImageComposer

    ThroughputPlotBuilder --> PlotConfigurator
    ResourcePlotBuilder --> PlotConfigurator
```

## Execution Flow

Chart generation proceeds through four phases:

```mermaid
sequenceDiagram
    participant Client
    participant CG as ChartGenerator
    participant DL as ChartDataLoader
    participant TPB as ThroughputPlotBuilder
    participant RPB as ResourcePlotBuilder
    participant POR as PhaseOverlayRenderer
    participant CIC as ChartImageComposer

    Client->>CG: GenerateChartAsync(outputPath, testReport)

    rect rgb(240, 248, 255)
        Note over DL: Phase 1: Load Data
        CG->>DL: LoadThroughputReport (events, API)
        CG->>DL: LoadResourceReport (service, rabbitmq, postgres, system)
    end

    rect rgb(255, 248, 240)
        Note over TPB,RPB: Phase 2: Build Plots
        CG->>TPB: Build throughput plot
        loop 4 resource types
            CG->>RPB: Build resource plot
        end
    end

    rect rgb(240, 255, 240)
        Note over POR: Phase 3: Add Overlays
        CG->>POR: AddPhaseBoundaries (all plots)
        CG->>POR: AddPhaseLabels + AddTitle (top plot)
    end

    rect rgb(255, 240, 255)
        Note over CIC: Phase 4: Render & Combine
        CG->>CIC: RenderPlotToBitmap (5 plots)
        CG->>CIC: CombineAndSave (vertical stack)
    end

    CG-->>Client: Task completed
```

## File Structure

```
ChartGeneration/
├── ChartGenerator.cs              # Orchestrator
├── ChartColors.cs                 # Color constants
├── Configuration/
│   └── ChartConfig.cs             # Immutable config records
├── DataLoading/
│   └── ChartDataLoader.cs         # JSON file parsing
├── ImageComposition/
│   └── ChartImageComposer.cs      # Bitmap rendering and stacking
├── PlotBuilders/
│   ├── ThroughputPlotBuilder.cs   # Events/API throughput subplot
│   └── ResourcePlotBuilder.cs     # CPU/RAM dual-axis subplot
└── PlotConfiguration/
    ├── PlotConfigurator.cs        # Shared plot setup (grid, legend, axes)
    └── PhaseOverlayRenderer.cs    # Phase boundaries and labels
```

## Configuration

All styling is centralized in `ChartConfig`, which composes:

- **FontConfig** - Font family, sizes for title/axis/legend/phase labels
- **LineConfig** - Line widths, fill alphas
- **PlotDimensions** - Width, height, padding
- **GridConfig** - Grid line color and alpha

Each config type is an immutable `record` with a static `Default` instance, allowing easy customization via `with` expressions.

## Usage

```csharp
var chartGenerator = serviceProvider.GetRequiredService<IChartGenerator>();

await chartGenerator.GenerateChartAsync(
    outputPath: "/path/to/test-report.chart.png",
    testReport: testReport);
```

The chart generator reads JSON data files from the same directory as the output path:
- `*.events-throughput.json`
- `*.api-throughput.json`
- `*.resource-metrics.json`
- `*.rabbitmq-metrics.json`
- `*.postgres-metrics.json`
- `*.system-metrics.json`
