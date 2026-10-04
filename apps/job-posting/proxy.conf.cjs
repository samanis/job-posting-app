// Set JOB_POSTING_API_URL before npm start when an API is available.
const target = process.env.JOB_POSTING_API_URL;
module.exports = target ? { '/api/**': { target, changeOrigin: true } } : {};
