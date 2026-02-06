using Dunet;

namespace PerformanceTester.ApiLoadTesting;

[Union]
internal partial record ParseLineError
{
    partial record EmptyInput;
    partial record InvalidJson(string RawLine);
    partial record IrrelevantMetric(string MetricName);
    partial record NonPointMetric(string MetricType);
}
