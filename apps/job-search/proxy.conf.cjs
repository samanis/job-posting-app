const target = process.env.JOB_SEARCH_API_URL;
module.exports = target ? { '/api/**': { target, changeOrigin: true } } : {};
