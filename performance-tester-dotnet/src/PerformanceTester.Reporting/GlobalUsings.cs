// File-scoped namespace
global using System;
global using System.Collections.Generic;
global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Globalization;
global using System.IO;
global using System.Linq;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Tasks;

// Common Library
global using PerformanceTester.Common;

// MathNet.Numerics
global using MathNet.Numerics.Statistics;

// ScottPlot
global using ScottPlot;

// Phase 2 Models
global using PerformanceTester.EventPublishing;
global using PerformanceTester.EventConsuming;
global using PerformanceTester.ProcessMonitoring;
global using PerformanceTester.DockerMonitoring;
global using PerformanceTester.ApiLoadTesting;
