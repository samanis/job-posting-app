// Local Docker API by default; override for another development backend.
const target = process.env.JOB_SEARCH_API_URL || 'http://localhost:5101';
module.exports = { '/api/**': { target, changeOrigin: true } };
