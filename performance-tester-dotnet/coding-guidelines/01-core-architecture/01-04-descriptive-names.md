# 01-04. Descriptive Function Names

Functions that configure should say **what** they configure. Avoid generic names that hide behavior.

```csharp
// ✅ Good - says exactly what it does
ConfigureLeftAxisLabel(plot, text, color, font)
AddScatterWithFill(plot, timestamps, values, color, lineWidth, fillAlpha, legendText)

// ❌ Avoid - vague names
ConfigureAxis(plot)
AddData(plot, data)
```
