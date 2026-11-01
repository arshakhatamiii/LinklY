import http from 'k6/http';
import { check } from 'k6';

export const options = {
  stages: [
    { duration: '10s', target: 50 }, // Ramp up to 50 VUs
    { duration: '20s', target: 50 }, // Hold 50 VUs for 20s
  ],
  thresholds: {
    http_req_failed: ['rate<0.01'],   // Error rate should be less than 1%
    http_req_duration: ['p(95)<100'], // 95% of requests should be below 100ms
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5100';

export function setup() {
  // Create a target link before running the load test
  const payload = JSON.stringify({
    url: 'https://github.com/dotnet/aspnetcore',
    alias: 'loadtest-baseline',
  });

  const res = http.post(`${BASE_URL}/api/links`, payload, {
    headers: { 'Content-Type': 'application/json' },
  });

  if (res.status === 201) {
    return { code: res.json('code') };
  }

  // If alias already exists (status 409), reuse it
  return { code: 'loadtest-baseline' };
}

export default function (data) {
  const code = data.code || 'loadtest-baseline';

  // Issue GET request without following redirects (redirects: 0)
  const res = http.get(`${BASE_URL}/${code}`, {
    redirects: 0,
  });

  check(res, {
    'status is 302': (r) => r.status === 302,
    'has location header': (r) => r.headers['Location'] !== undefined,
  });
}
