import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:5100';
const SCENARIO = __ENV.SCENARIO || 'uniform'; // 'uniform' or 'hotset'
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
        { target: 3000, duration: '20s' },
        { target: 6000, duration: '20s' },
        { target: 9000, duration: '20s' },
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
  console.log(`Seeding ${N} short links against ${BASE}...`);

  for (let i = 0; i < N; i++) {
    const res = http.post(
      `${BASE}/api/links`,
      JSON.stringify({ url: `https://example.com/page/${i}` }),
      { headers: { 'Content-Type': 'application/json' } }
    );

    if (res.status === 201) {
      codes.push(res.json('code'));
    }
  }

  console.log(`Seeded ${codes.length} links successfully. Scenario mode: ${SCENARIO}`);
  return { codes, scenario: SCENARIO };
}

export default function (data) {
  let code;

  if (data.scenario === 'hotset') {
    // 80% of traffic hits 20% of links (Pareto / Hot-set distribution)
    const isHot = Math.random() < 0.8;
    const hotCutoff = Math.floor(data.codes.length * 0.2);

    if (isHot && hotCutoff > 0) {
      code = data.codes[Math.floor(Math.random() * hotCutoff)];
    } else {
      code = data.codes[hotCutoff + Math.floor(Math.random() * (data.codes.length - hotCutoff))];
    }
  } else {
    // Uniformly random codes across entire set
    code = data.codes[Math.floor(Math.random() * data.codes.length)];
  }

  const res = http.get(`${BASE}/${code}`, { redirects: 0 });
  check(res, { 'status is 302': (r) => r.status === 302 });
}
