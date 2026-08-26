/// <binding BeforeBuild='build:sass' />
var gulp = require('gulp');
var gulpLoadPlugins = require('gulp-load-plugins');
var $ = gulpLoadPlugins();
var gcmq = require('gulp-group-css-media-queries');

gulp.task('build:sass', function () {
    return gulp.src('./wwwroot/scss/**/*.scss')
        .pipe($.plumber())
        .pipe($.sourcemaps.init())
        .pipe($.sass({
            sourcemap: true,
            outputStyle: 'expanded',
            precision: 10,
            includePaths: ['.']
        }).on('error', $.sass.logError))
        .pipe(gcmq())
        .pipe($.autoprefixer({ browsers: ['> 1%', 'last 2 versions', 'Firefox ESR'] }))
        .pipe($.cssnano())
        .pipe($.sourcemaps.write('.'))
        .pipe(gulp.dest("./wwwroot/css"));
});

gulp.task('watch:sass', function () {
    gulp.watch('./wwwroot/scss/**/*.scss', ['build:sass']);
});