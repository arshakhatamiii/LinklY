import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:5100';
const N = 5000;

export const options = {
  scenarios: {
    stress: {
      executor: 'ramping-arrival-rate',
      startRate: 1000,
      timeUnit: '1s',
      preAllocatedVUs: 200,
      maxVUs: 1000,
      stages: [
        { target: 3000,  duration: '20s' },
        { target: 6000,  duration: '20s' },
        { target: 9000,  duration: '20s' },
        { target: 12000, duration: '20s' },
        { target: 15000, duration: '20s' },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<100'],
  },
};

export function setup() {
  const codes = [];
  for (let i = 0; i < N; i++) {
    const r = http.post(
      `${BASE}/api/links`,
      JSON.stringify({ url: `https://example.com/page/${i}` }),
      { headers: { 'Content-Type': 'application/json' } }
    );
    codes.push(r.json('code')); // Matches our response field name
  }
  return { codes };
}

export default function (data) {
  const code = data.codes[Math.floor(Math.random() * data.codes.length)];
  const res = http.get(`${BASE}/${code}`, { redirects: 0 });
  check(res, { 'status is 302': (r) => r.status === 302 });
}
