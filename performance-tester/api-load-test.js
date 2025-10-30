import http from 'k6/http';
import { check } from 'k6';

export let options = {
  // These will be overridden by CLI arguments
  duration: '30s',
  vus: 1,  // Virtual users (equivalent to concurrent connections)

  // Disable thresholds (we'll analyze metrics in Python)
  thresholds: {},

  // Configure summary trend stats
  summaryTrendStats: ['min', 'avg', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
};

export default function() {
  // Get API URL from environment variable (set by Python)
  const apiUrl = __ENV.API_URL || 'http://localhost:8080/kpi';

  // Make HTTP GET request
  const res = http.get(apiUrl);

  // Check for successful response
  check(res, {
    'status is 2xx': (r) => r.status >= 200 && r.status < 300,
  });
}
