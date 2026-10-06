// Local Docker API by default; override for another development backend.
const target = process.env.JOB_POSTING_API_URL || 'http://localhost:5000';
module.exports = { '/api/**': { target, changeOrigin: true } };
