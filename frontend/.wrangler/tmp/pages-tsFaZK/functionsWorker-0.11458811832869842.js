var __defProp = Object.defineProperty;
var __name = (target, value) => __defProp(target, "name", { value, configurable: true });

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/_internal/utils.mjs
// @__NO_SIDE_EFFECTS__
function createNotImplementedError(name) {
  return new Error(`[unenv] ${name} is not implemented yet!`);
}
__name(createNotImplementedError, "createNotImplementedError");
// @__NO_SIDE_EFFECTS__
function notImplemented(name) {
  const fn = /* @__PURE__ */ __name(() => {
    throw /* @__PURE__ */ createNotImplementedError(name);
  }, "fn");
  return Object.assign(fn, { __unenv__: true });
}
__name(notImplemented, "notImplemented");
// @__NO_SIDE_EFFECTS__
function notImplementedClass(name) {
  return class {
    __unenv__ = true;
    constructor() {
      throw new Error(`[unenv] ${name} is not implemented yet!`);
    }
  };
}
__name(notImplementedClass, "notImplementedClass");

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/node/internal/perf_hooks/performance.mjs
var _timeOrigin = globalThis.performance?.timeOrigin ?? Date.now();
var _performanceNow = globalThis.performance?.now ? globalThis.performance.now.bind(globalThis.performance) : () => Date.now() - _timeOrigin;
var nodeTiming = {
  name: "node",
  entryType: "node",
  startTime: 0,
  duration: 0,
  nodeStart: 0,
  v8Start: 0,
  bootstrapComplete: 0,
  environment: 0,
  loopStart: 0,
  loopExit: 0,
  idleTime: 0,
  uvMetricsInfo: {
    loopCount: 0,
    events: 0,
    eventsWaiting: 0
  },
  detail: void 0,
  toJSON() {
    return this;
  }
};
var PerformanceEntry = class {
  static {
    __name(this, "PerformanceEntry");
  }
  __unenv__ = true;
  detail;
  entryType = "event";
  name;
  startTime;
  constructor(name, options) {
    this.name = name;
    this.startTime = options?.startTime || _performanceNow();
    this.detail = options?.detail;
  }
  get duration() {
    return _performanceNow() - this.startTime;
  }
  toJSON() {
    return {
      name: this.name,
      entryType: this.entryType,
      startTime: this.startTime,
      duration: this.duration,
      detail: this.detail
    };
  }
};
var PerformanceMark = class PerformanceMark2 extends PerformanceEntry {
  static {
    __name(this, "PerformanceMark");
  }
  entryType = "mark";
  constructor() {
    super(...arguments);
  }
  get duration() {
    return 0;
  }
};
var PerformanceMeasure = class extends PerformanceEntry {
  static {
    __name(this, "PerformanceMeasure");
  }
  entryType = "measure";
};
var PerformanceResourceTiming = class extends PerformanceEntry {
  static {
    __name(this, "PerformanceResourceTiming");
  }
  entryType = "resource";
  serverTiming = [];
  connectEnd = 0;
  connectStart = 0;
  decodedBodySize = 0;
  domainLookupEnd = 0;
  domainLookupStart = 0;
  encodedBodySize = 0;
  fetchStart = 0;
  initiatorType = "";
  name = "";
  nextHopProtocol = "";
  redirectEnd = 0;
  redirectStart = 0;
  requestStart = 0;
  responseEnd = 0;
  responseStart = 0;
  secureConnectionStart = 0;
  startTime = 0;
  transferSize = 0;
  workerStart = 0;
  responseStatus = 0;
};
var PerformanceObserverEntryList = class {
  static {
    __name(this, "PerformanceObserverEntryList");
  }
  __unenv__ = true;
  getEntries() {
    return [];
  }
  getEntriesByName(_name, _type) {
    return [];
  }
  getEntriesByType(type) {
    return [];
  }
};
var Performance = class {
  static {
    __name(this, "Performance");
  }
  __unenv__ = true;
  timeOrigin = _timeOrigin;
  eventCounts = /* @__PURE__ */ new Map();
  _entries = [];
  _resourceTimingBufferSize = 0;
  navigation = void 0;
  timing = void 0;
  timerify(_fn, _options) {
    throw createNotImplementedError("Performance.timerify");
  }
  get nodeTiming() {
    return nodeTiming;
  }
  eventLoopUtilization() {
    return {};
  }
  markResourceTiming() {
    return new PerformanceResourceTiming("");
  }
  onresourcetimingbufferfull = null;
  now() {
    if (this.timeOrigin === _timeOrigin) {
      return _performanceNow();
    }
    return Date.now() - this.timeOrigin;
  }
  clearMarks(markName) {
    this._entries = markName ? this._entries.filter((e) => e.name !== markName) : this._entries.filter((e) => e.entryType !== "mark");
  }
  clearMeasures(measureName) {
    this._entries = measureName ? this._entries.filter((e) => e.name !== measureName) : this._entries.filter((e) => e.entryType !== "measure");
  }
  clearResourceTimings() {
    this._entries = this._entries.filter((e) => e.entryType !== "resource" || e.entryType !== "navigation");
  }
  getEntries() {
    return this._entries;
  }
  getEntriesByName(name, type) {
    return this._entries.filter((e) => e.name === name && (!type || e.entryType === type));
  }
  getEntriesByType(type) {
    return this._entries.filter((e) => e.entryType === type);
  }
  mark(name, options) {
    const entry = new PerformanceMark(name, options);
    this._entries.push(entry);
    return entry;
  }
  measure(measureName, startOrMeasureOptions, endMark) {
    let start;
    let end;
    if (typeof startOrMeasureOptions === "string") {
      start = this.getEntriesByName(startOrMeasureOptions, "mark")[0]?.startTime;
      end = this.getEntriesByName(endMark, "mark")[0]?.startTime;
    } else {
      start = Number.parseFloat(startOrMeasureOptions?.start) || this.now();
      end = Number.parseFloat(startOrMeasureOptions?.end) || this.now();
    }
    const entry = new PerformanceMeasure(measureName, {
      startTime: start,
      detail: {
        start,
        end
      }
    });
    this._entries.push(entry);
    return entry;
  }
  setResourceTimingBufferSize(maxSize) {
    this._resourceTimingBufferSize = maxSize;
  }
  addEventListener(type, listener, options) {
    throw createNotImplementedError("Performance.addEventListener");
  }
  removeEventListener(type, listener, options) {
    throw createNotImplementedError("Performance.removeEventListener");
  }
  dispatchEvent(event) {
    throw createNotImplementedError("Performance.dispatchEvent");
  }
  toJSON() {
    return this;
  }
};
var PerformanceObserver = class {
  static {
    __name(this, "PerformanceObserver");
  }
  __unenv__ = true;
  static supportedEntryTypes = [];
  _callback = null;
  constructor(callback) {
    this._callback = callback;
  }
  takeRecords() {
    return [];
  }
  disconnect() {
    throw createNotImplementedError("PerformanceObserver.disconnect");
  }
  observe(options) {
    throw createNotImplementedError("PerformanceObserver.observe");
  }
  bind(fn) {
    return fn;
  }
  runInAsyncScope(fn, thisArg, ...args) {
    return fn.call(thisArg, ...args);
  }
  asyncId() {
    return 0;
  }
  triggerAsyncId() {
    return 0;
  }
  emitDestroy() {
    return this;
  }
};
var performance = globalThis.performance && "addEventListener" in globalThis.performance ? globalThis.performance : new Performance();

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/@cloudflare/unenv-preset/dist/runtime/polyfill/performance.mjs
if (!("__unenv__" in performance)) {
  const proto = Performance.prototype;
  for (const key of Object.getOwnPropertyNames(proto)) {
    if (key !== "constructor" && !(key in performance)) {
      const desc = Object.getOwnPropertyDescriptor(proto, key);
      if (desc) {
        Object.defineProperty(performance, key, desc);
      }
    }
  }
}
globalThis.performance = performance;
globalThis.Performance = Performance;
globalThis.PerformanceEntry = PerformanceEntry;
globalThis.PerformanceMark = PerformanceMark;
globalThis.PerformanceMeasure = PerformanceMeasure;
globalThis.PerformanceObserver = PerformanceObserver;
globalThis.PerformanceObserverEntryList = PerformanceObserverEntryList;
globalThis.PerformanceResourceTiming = PerformanceResourceTiming;

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/node/console.mjs
import { Writable } from "node:stream";

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/mock/noop.mjs
var noop_default = Object.assign(() => {
}, { __unenv__: true });

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/node/console.mjs
var _console = globalThis.console;
var _ignoreErrors = true;
var _stderr = new Writable();
var _stdout = new Writable();
var log = _console?.log ?? noop_default;
var info = _console?.info ?? log;
var trace = _console?.trace ?? info;
var debug = _console?.debug ?? log;
var table = _console?.table ?? log;
var error = _console?.error ?? log;
var warn = _console?.warn ?? error;
var createTask = _console?.createTask ?? /* @__PURE__ */ notImplemented("console.createTask");
var clear = _console?.clear ?? noop_default;
var count = _console?.count ?? noop_default;
var countReset = _console?.countReset ?? noop_default;
var dir = _console?.dir ?? noop_default;
var dirxml = _console?.dirxml ?? noop_default;
var group = _console?.group ?? noop_default;
var groupEnd = _console?.groupEnd ?? noop_default;
var groupCollapsed = _console?.groupCollapsed ?? noop_default;
var profile = _console?.profile ?? noop_default;
var profileEnd = _console?.profileEnd ?? noop_default;
var time = _console?.time ?? noop_default;
var timeEnd = _console?.timeEnd ?? noop_default;
var timeLog = _console?.timeLog ?? noop_default;
var timeStamp = _console?.timeStamp ?? noop_default;
var Console = _console?.Console ?? /* @__PURE__ */ notImplementedClass("console.Console");
var _times = /* @__PURE__ */ new Map();
var _stdoutErrorHandler = noop_default;
var _stderrErrorHandler = noop_default;

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/@cloudflare/unenv-preset/dist/runtime/node/console.mjs
var workerdConsole = globalThis["console"];
var {
  assert,
  clear: clear2,
  // @ts-expect-error undocumented public API
  context,
  count: count2,
  countReset: countReset2,
  // @ts-expect-error undocumented public API
  createTask: createTask2,
  debug: debug2,
  dir: dir2,
  dirxml: dirxml2,
  error: error2,
  group: group2,
  groupCollapsed: groupCollapsed2,
  groupEnd: groupEnd2,
  info: info2,
  log: log2,
  profile: profile2,
  profileEnd: profileEnd2,
  table: table2,
  time: time2,
  timeEnd: timeEnd2,
  timeLog: timeLog2,
  timeStamp: timeStamp2,
  trace: trace2,
  warn: warn2
} = workerdConsole;
Object.assign(workerdConsole, {
  Console,
  _ignoreErrors,
  _stderr,
  _stderrErrorHandler,
  _stdout,
  _stdoutErrorHandler,
  _times
});
var console_default = workerdConsole;

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/wrangler/_virtual_unenv_global_polyfill-@cloudflare-unenv-preset-node-console
globalThis.console = console_default;

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/node/internal/process/hrtime.mjs
var hrtime = /* @__PURE__ */ Object.assign(/* @__PURE__ */ __name(function hrtime2(startTime) {
  const now = Date.now();
  const seconds = Math.trunc(now / 1e3);
  const nanos = now % 1e3 * 1e6;
  if (startTime) {
    let diffSeconds = seconds - startTime[0];
    let diffNanos = nanos - startTime[0];
    if (diffNanos < 0) {
      diffSeconds = diffSeconds - 1;
      diffNanos = 1e9 + diffNanos;
    }
    return [diffSeconds, diffNanos];
  }
  return [seconds, nanos];
}, "hrtime"), { bigint: /* @__PURE__ */ __name(function bigint() {
  return BigInt(Date.now() * 1e6);
}, "bigint") });

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/node/internal/process/process.mjs
import { EventEmitter } from "node:events";

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/node/internal/tty/read-stream.mjs
var ReadStream = class {
  static {
    __name(this, "ReadStream");
  }
  fd;
  isRaw = false;
  isTTY = false;
  constructor(fd) {
    this.fd = fd;
  }
  setRawMode(mode) {
    this.isRaw = mode;
    return this;
  }
};

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/node/internal/tty/write-stream.mjs
var WriteStream = class {
  static {
    __name(this, "WriteStream");
  }
  fd;
  columns = 80;
  rows = 24;
  isTTY = false;
  constructor(fd) {
    this.fd = fd;
  }
  clearLine(dir3, callback) {
    callback && callback();
    return false;
  }
  clearScreenDown(callback) {
    callback && callback();
    return false;
  }
  cursorTo(x, y, callback) {
    callback && typeof callback === "function" && callback();
    return false;
  }
  moveCursor(dx, dy, callback) {
    callback && callback();
    return false;
  }
  getColorDepth(env2) {
    return 1;
  }
  hasColors(count3, env2) {
    return false;
  }
  getWindowSize() {
    return [this.columns, this.rows];
  }
  write(str, encoding, cb) {
    if (str instanceof Uint8Array) {
      str = new TextDecoder().decode(str);
    }
    try {
      console.log(str);
    } catch {
    }
    cb && typeof cb === "function" && cb();
    return false;
  }
};

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/node/internal/process/node-version.mjs
var NODE_VERSION = "22.14.0";

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/unenv/dist/runtime/node/internal/process/process.mjs
var Process = class _Process extends EventEmitter {
  static {
    __name(this, "Process");
  }
  env;
  hrtime;
  nextTick;
  constructor(impl) {
    super();
    this.env = impl.env;
    this.hrtime = impl.hrtime;
    this.nextTick = impl.nextTick;
    for (const prop of [...Object.getOwnPropertyNames(_Process.prototype), ...Object.getOwnPropertyNames(EventEmitter.prototype)]) {
      const value = this[prop];
      if (typeof value === "function") {
        this[prop] = value.bind(this);
      }
    }
  }
  // --- event emitter ---
  emitWarning(warning, type, code) {
    console.warn(`${code ? `[${code}] ` : ""}${type ? `${type}: ` : ""}${warning}`);
  }
  emit(...args) {
    return super.emit(...args);
  }
  listeners(eventName) {
    return super.listeners(eventName);
  }
  // --- stdio (lazy initializers) ---
  #stdin;
  #stdout;
  #stderr;
  get stdin() {
    return this.#stdin ??= new ReadStream(0);
  }
  get stdout() {
    return this.#stdout ??= new WriteStream(1);
  }
  get stderr() {
    return this.#stderr ??= new WriteStream(2);
  }
  // --- cwd ---
  #cwd = "/";
  chdir(cwd2) {
    this.#cwd = cwd2;
  }
  cwd() {
    return this.#cwd;
  }
  // --- dummy props and getters ---
  arch = "";
  platform = "";
  argv = [];
  argv0 = "";
  execArgv = [];
  execPath = "";
  title = "";
  pid = 200;
  ppid = 100;
  get version() {
    return `v${NODE_VERSION}`;
  }
  get versions() {
    return { node: NODE_VERSION };
  }
  get allowedNodeEnvironmentFlags() {
    return /* @__PURE__ */ new Set();
  }
  get sourceMapsEnabled() {
    return false;
  }
  get debugPort() {
    return 0;
  }
  get throwDeprecation() {
    return false;
  }
  get traceDeprecation() {
    return false;
  }
  get features() {
    return {};
  }
  get release() {
    return {};
  }
  get connected() {
    return false;
  }
  get config() {
    return {};
  }
  get moduleLoadList() {
    return [];
  }
  constrainedMemory() {
    return 0;
  }
  availableMemory() {
    return 0;
  }
  uptime() {
    return 0;
  }
  resourceUsage() {
    return {};
  }
  // --- noop methods ---
  ref() {
  }
  unref() {
  }
  // --- unimplemented methods ---
  umask() {
    throw createNotImplementedError("process.umask");
  }
  getBuiltinModule() {
    return void 0;
  }
  getActiveResourcesInfo() {
    throw createNotImplementedError("process.getActiveResourcesInfo");
  }
  exit() {
    throw createNotImplementedError("process.exit");
  }
  reallyExit() {
    throw createNotImplementedError("process.reallyExit");
  }
  kill() {
    throw createNotImplementedError("process.kill");
  }
  abort() {
    throw createNotImplementedError("process.abort");
  }
  dlopen() {
    throw createNotImplementedError("process.dlopen");
  }
  setSourceMapsEnabled() {
    throw createNotImplementedError("process.setSourceMapsEnabled");
  }
  loadEnvFile() {
    throw createNotImplementedError("process.loadEnvFile");
  }
  disconnect() {
    throw createNotImplementedError("process.disconnect");
  }
  cpuUsage() {
    throw createNotImplementedError("process.cpuUsage");
  }
  setUncaughtExceptionCaptureCallback() {
    throw createNotImplementedError("process.setUncaughtExceptionCaptureCallback");
  }
  hasUncaughtExceptionCaptureCallback() {
    throw createNotImplementedError("process.hasUncaughtExceptionCaptureCallback");
  }
  initgroups() {
    throw createNotImplementedError("process.initgroups");
  }
  openStdin() {
    throw createNotImplementedError("process.openStdin");
  }
  assert() {
    throw createNotImplementedError("process.assert");
  }
  binding() {
    throw createNotImplementedError("process.binding");
  }
  // --- attached interfaces ---
  permission = { has: /* @__PURE__ */ notImplemented("process.permission.has") };
  report = {
    directory: "",
    filename: "",
    signal: "SIGUSR2",
    compact: false,
    reportOnFatalError: false,
    reportOnSignal: false,
    reportOnUncaughtException: false,
    getReport: /* @__PURE__ */ notImplemented("process.report.getReport"),
    writeReport: /* @__PURE__ */ notImplemented("process.report.writeReport")
  };
  finalization = {
    register: /* @__PURE__ */ notImplemented("process.finalization.register"),
    unregister: /* @__PURE__ */ notImplemented("process.finalization.unregister"),
    registerBeforeExit: /* @__PURE__ */ notImplemented("process.finalization.registerBeforeExit")
  };
  memoryUsage = Object.assign(() => ({
    arrayBuffers: 0,
    rss: 0,
    external: 0,
    heapTotal: 0,
    heapUsed: 0
  }), { rss: /* @__PURE__ */ __name(() => 0, "rss") });
  // --- undefined props ---
  mainModule = void 0;
  domain = void 0;
  // optional
  send = void 0;
  exitCode = void 0;
  channel = void 0;
  getegid = void 0;
  geteuid = void 0;
  getgid = void 0;
  getgroups = void 0;
  getuid = void 0;
  setegid = void 0;
  seteuid = void 0;
  setgid = void 0;
  setgroups = void 0;
  setuid = void 0;
  // internals
  _events = void 0;
  _eventsCount = void 0;
  _exiting = void 0;
  _maxListeners = void 0;
  _debugEnd = void 0;
  _debugProcess = void 0;
  _fatalException = void 0;
  _getActiveHandles = void 0;
  _getActiveRequests = void 0;
  _kill = void 0;
  _preload_modules = void 0;
  _rawDebug = void 0;
  _startProfilerIdleNotifier = void 0;
  _stopProfilerIdleNotifier = void 0;
  _tickCallback = void 0;
  _disconnect = void 0;
  _handleQueue = void 0;
  _pendingMessage = void 0;
  _channel = void 0;
  _send = void 0;
  _linkedBinding = void 0;
};

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/@cloudflare/unenv-preset/dist/runtime/node/process.mjs
var globalProcess = globalThis["process"];
var getBuiltinModule = globalProcess.getBuiltinModule;
var workerdProcess = getBuiltinModule("node:process");
var unenvProcess = new Process({
  env: globalProcess.env,
  hrtime,
  // `nextTick` is available from workerd process v1
  nextTick: workerdProcess.nextTick
});
var { exit, features, platform } = workerdProcess;
var {
  _channel,
  _debugEnd,
  _debugProcess,
  _disconnect,
  _events,
  _eventsCount,
  _exiting,
  _fatalException,
  _getActiveHandles,
  _getActiveRequests,
  _handleQueue,
  _kill,
  _linkedBinding,
  _maxListeners,
  _pendingMessage,
  _preload_modules,
  _rawDebug,
  _send,
  _startProfilerIdleNotifier,
  _stopProfilerIdleNotifier,
  _tickCallback,
  abort,
  addListener,
  allowedNodeEnvironmentFlags,
  arch,
  argv,
  argv0,
  assert: assert2,
  availableMemory,
  binding,
  channel,
  chdir,
  config,
  connected,
  constrainedMemory,
  cpuUsage,
  cwd,
  debugPort,
  disconnect,
  dlopen,
  domain,
  emit,
  emitWarning,
  env,
  eventNames,
  execArgv,
  execPath,
  exitCode,
  finalization,
  getActiveResourcesInfo,
  getegid,
  geteuid,
  getgid,
  getgroups,
  getMaxListeners,
  getuid,
  hasUncaughtExceptionCaptureCallback,
  hrtime: hrtime3,
  initgroups,
  kill,
  listenerCount,
  listeners,
  loadEnvFile,
  mainModule,
  memoryUsage,
  moduleLoadList,
  nextTick,
  off,
  on,
  once,
  openStdin,
  permission,
  pid,
  ppid,
  prependListener,
  prependOnceListener,
  rawListeners,
  reallyExit,
  ref,
  release,
  removeAllListeners,
  removeListener,
  report,
  resourceUsage,
  send,
  setegid,
  seteuid,
  setgid,
  setgroups,
  setMaxListeners,
  setSourceMapsEnabled,
  setuid,
  setUncaughtExceptionCaptureCallback,
  sourceMapsEnabled,
  stderr,
  stdin,
  stdout,
  throwDeprecation,
  title,
  traceDeprecation,
  umask,
  unref,
  uptime,
  version,
  versions
} = unenvProcess;
var _process = {
  abort,
  addListener,
  allowedNodeEnvironmentFlags,
  hasUncaughtExceptionCaptureCallback,
  setUncaughtExceptionCaptureCallback,
  loadEnvFile,
  sourceMapsEnabled,
  arch,
  argv,
  argv0,
  chdir,
  config,
  connected,
  constrainedMemory,
  availableMemory,
  cpuUsage,
  cwd,
  debugPort,
  dlopen,
  disconnect,
  emit,
  emitWarning,
  env,
  eventNames,
  execArgv,
  execPath,
  exit,
  finalization,
  features,
  getBuiltinModule,
  getActiveResourcesInfo,
  getMaxListeners,
  hrtime: hrtime3,
  kill,
  listeners,
  listenerCount,
  memoryUsage,
  nextTick,
  on,
  off,
  once,
  pid,
  platform,
  ppid,
  prependListener,
  prependOnceListener,
  rawListeners,
  release,
  removeAllListeners,
  removeListener,
  report,
  resourceUsage,
  setMaxListeners,
  setSourceMapsEnabled,
  stderr,
  stdin,
  stdout,
  title,
  throwDeprecation,
  traceDeprecation,
  umask,
  uptime,
  version,
  versions,
  // @ts-expect-error old API
  domain,
  initgroups,
  moduleLoadList,
  reallyExit,
  openStdin,
  assert: assert2,
  binding,
  send,
  exitCode,
  channel,
  getegid,
  geteuid,
  getgid,
  getgroups,
  getuid,
  setegid,
  seteuid,
  setgid,
  setgroups,
  setuid,
  permission,
  mainModule,
  _events,
  _eventsCount,
  _exiting,
  _maxListeners,
  _debugEnd,
  _debugProcess,
  _fatalException,
  _getActiveHandles,
  _getActiveRequests,
  _kill,
  _preload_modules,
  _rawDebug,
  _startProfilerIdleNotifier,
  _stopProfilerIdleNotifier,
  _tickCallback,
  _disconnect,
  _handleQueue,
  _pendingMessage,
  _channel,
  _send,
  _linkedBinding
};
var process_default = _process;

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/wrangler/_virtual_unenv_global_polyfill-@cloudflare-unenv-preset-node-process
globalThis.process = process_default;

// _comun/seo.js
function origenDelSitio(env2) {
  const crudo = typeof env2?.SITIO_ORIGEN === "string" ? env2.SITIO_ORIGEN.trim().replace(/\/+$/, "") : "";
  return /^https:\/\/[a-z0-9.-]+(:\d+)?$/i.test(crudo) ? crudo : "https://zass.cl";
}
__name(origenDelSitio, "origenDelSitio");
var CABECERAS_DE_SEGURIDAD = {
  "Content-Security-Policy": "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'",
  "X-Frame-Options": "DENY",
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "strict-origin-when-cross-origin",
  "Permissions-Policy": "camera=(), microphone=(), payment=(), geolocation=(self)",
  "Strict-Transport-Security": "max-age=31536000; includeSubDomains"
};
function escaparHtml(valor) {
  return String(valor ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;").replace(/`/g, "&#96;");
}
__name(escaparHtml, "escaparHtml");
function jsonParaScript(objeto) {
  return JSON.stringify(objeto).replace(/</g, "\\u003c").replace(/>/g, "\\u003e").replace(/&/g, "\\u0026").replace(/\u2028/g, "\\u2028").replace(/\u2029/g, "\\u2029");
}
__name(jsonParaScript, "jsonParaScript");
function limpiarTexto(valor) {
  return String(valor ?? "").replace(/[\u0000-\u001f\u007f\u2028\u2029]+/g, " ").replace(/\s+/g, " ").trim();
}
__name(limpiarTexto, "limpiarTexto");
function recortar(texto, maximo) {
  const t = limpiarTexto(texto);
  if (t.length <= maximo) return t;
  const corte = t.slice(0, maximo - 1);
  const ultimoEspacio = corte.lastIndexOf(" ");
  return `${(ultimoEspacio > maximo * 0.6 ? corte.slice(0, ultimoEspacio) : corte).replace(/[\s,.;:]+$/, "")}\u2026`;
}
__name(recortar, "recortar");
var NOMBRE = /* @__PURE__ */ __name((v) => typeof v === "string" ? limpiarTexto(v) : "", "NOMBRE");
var PROMEDIO = /* @__PURE__ */ __name((v) => typeof v === "number" && Number.isFinite(v) && v >= 1 && v <= 5 ? v : null, "PROMEDIO");
var RESENAS = /* @__PURE__ */ __name((v) => typeof v === "number" && Number.isInteger(v) && v >= 0 ? v : 0, "RESENAS");
function datosDeFicha(ficha) {
  if (ficha === null || typeof ficha !== "object") return null;
  const nombre = NOMBRE(ficha.nombrePublico);
  if (nombre === "") return null;
  const oficio = NOMBRE(ficha.rubro?.nombre);
  const zonas = Array.isArray(ficha.zonas) ? ficha.zonas.filter((z) => z && typeof z === "object").map((z) => ({ nombre: NOMBRE(z.nombre), principal: z.esPrincipal === true })).filter((z) => z.nombre !== "") : [];
  const principal = (zonas.find((z) => z.principal) ?? zonas[0])?.nombre ?? "";
  const descripcion = NOMBRE(ficha.descripcion);
  const indexable = ficha.suscripcionVigente !== false;
  const promedioCalificacion = PROMEDIO(ficha.promedioCalificacion);
  const totalResenas = RESENAS(ficha.totalResenas);
  return { nombre, oficio, zonas: zonas.map((z) => z.nombre), principal, descripcion, promedioCalificacion, totalResenas, indexable };
}
__name(datosDeFicha, "datosDeFicha");
function resumenDeOficio(d) {
  if (d.oficio && d.principal) return `${d.oficio} en ${d.principal}`;
  return d.oficio || (d.principal ? `En ${d.principal}` : "");
}
__name(resumenDeOficio, "resumenDeOficio");
function tituloDeFicha(d) {
  const resumen = resumenDeOficio(d);
  return recortar(resumen ? `${d.nombre} \xB7 ${resumen} \xB7 ZASS!` : `${d.nombre} \xB7 ZASS!`, 70);
}
__name(tituloDeFicha, "tituloDeFicha");
function descripcionDeFicha(d) {
  const resumen = resumenDeOficio(d);
  const base = d.descripcion ? resumen ? `${resumen}. ${d.descripcion}` : d.descripcion : resumen ? `${d.nombre}: ${resumen}. Mira su ficha y cont\xE1ctala directo en ZASS!.` : `Mira la ficha de ${d.nombre} en ZASS!.`;
  return recortar(base, 160);
}
__name(descripcionDeFicha, "descripcionDeFicha");
function metaDeFicha(d, slug, origen) {
  const url = `${origen}/prestadores/${encodeURIComponent(slug)}`;
  const titulo = tituloDeFicha(d);
  const descripcion = descripcionDeFicha(d);
  const imagen = `${origen}/og-zass.png`;
  const ld = {
    "@context": "https://schema.org",
    "@type": "ProfessionalService",
    name: d.nombre,
    url,
    description: descripcion,
    ...d.oficio ? { serviceType: d.oficio } : {},
    ...d.zonas.length > 0 ? { areaServed: d.zonas.map((z) => ({ "@type": "Place", name: z })) } : {},
    // RN-S5-11: el promedio es publico desde la TERCERA resena. Con menos, el JSON-LD va sin
    // `aggregateRating`: Google no debe ver un numero que la ficha visible no muestra.
    // `toFixed(1)` para que el JSON-LD diga exactamente lo mismo que la ficha ("4,3"), y no
    // "4.333333...": un rich result con un valor que la pagina no respalda es un rich result
    // que Google puede retirar.
    ...d.totalResenas >= 3 && d.promedioCalificacion !== null ? {
      aggregateRating: {
        "@type": "AggregateRating",
        ratingValue: Number(d.promedioCalificacion.toFixed(1)),
        reviewCount: d.totalResenas,
        bestRating: 5,
        worstRating: 1
      }
    } : {}
  };
  return [
    ...d.indexable ? [] : [`<meta name="robots" content="noindex, nofollow" />`],
    `<title>${escaparHtml(titulo)}</title>`,
    `<meta name="description" content="${escaparHtml(descripcion)}" />`,
    `<link rel="canonical" href="${escaparHtml(url)}" />`,
    `<meta property="og:type" content="profile" />`,
    `<meta property="og:site_name" content="ZASS!" />`,
    `<meta property="og:locale" content="es_CL" />`,
    `<meta property="og:title" content="${escaparHtml(titulo)}" />`,
    `<meta property="og:description" content="${escaparHtml(descripcion)}" />`,
    `<meta property="og:url" content="${escaparHtml(url)}" />`,
    `<meta property="og:image" content="${escaparHtml(imagen)}" />`,
    `<meta property="og:image:width" content="1200" />`,
    `<meta property="og:image:height" content="630" />`,
    `<meta name="twitter:card" content="summary_large_image" />`,
    `<meta name="twitter:title" content="${escaparHtml(titulo)}" />`,
    `<meta name="twitter:description" content="${escaparHtml(descripcion)}" />`,
    `<meta name="twitter:image" content="${escaparHtml(imagen)}" />`,
    ...d.indexable ? [`<script type="application/ld+json">${jsonParaScript(ld)}<\/script>`] : []
  ].join("\n    ");
}
__name(metaDeFicha, "metaDeFicha");
var META_NOINDEX = '<title>Ficha no encontrada \xB7 ZASS!</title>\n    <meta name="robots" content="noindex, nofollow" />';
var META_SIN_INDEXAR = '<meta name="robots" content="noindex, nofollow" />';
var MARCA_INICIO = "<!--seo:inicio-->";
var MARCA_FIN = "<!--seo:fin-->";
function inyectar(html, bloque) {
  const i = html.indexOf(MARCA_INICIO);
  const f = html.indexOf(MARCA_FIN);
  if (i === -1 || f === -1 || f < i) return html;
  return html.slice(0, i) + bloque + html.slice(f + MARCA_FIN.length);
}
__name(inyectar, "inyectar");
function cabecerasHtml(cacheControl) {
  return {
    ...CABECERAS_DE_SEGURIDAD,
    "Content-Type": "text/html; charset=utf-8",
    "Cache-Control": cacheControl
  };
}
__name(cabecerasHtml, "cabecerasHtml");

// prestadores/[slug].js
var FORMA_DE_SLUG = /^[a-z0-9][a-z0-9-]{0,119}$/;
var CACHE_FICHA = "public, max-age=0, s-maxage=300, stale-while-revalidate=600";
var CACHE_NO_EXISTE = "public, max-age=0, s-maxage=60";
var CACHE_API_CAIDA = "public, max-age=0, s-maxage=10";
var ESPERA_MAXIMA_MS = 4e3;
async function htmlDeLaApp(request, env2) {
  const respuesta = await env2.ASSETS.fetch(new URL("/", request.url));
  return respuesta.ok ? await respuesta.text() : null;
}
__name(htmlDeLaApp, "htmlDeLaApp");
async function onRequest({ request, env: env2, params }) {
  if (request.method !== "GET" && request.method !== "HEAD") return env2.ASSETS.fetch(request);
  const html = await htmlDeLaApp(request, env2);
  if (html === null) return env2.ASSETS.fetch(request);
  const responder2 = /* @__PURE__ */ __name((bloque, estado, cache) => new Response(request.method === "HEAD" ? null : bloque === null ? html : inyectar(html, bloque), {
    status: estado,
    headers: cabecerasHtml(cache)
  }), "responder");
  const slug = Array.isArray(params.slug) ? params.slug[0] : params.slug;
  if (typeof slug !== "string" || !FORMA_DE_SLUG.test(slug)) {
    return responder2(META_SIN_INDEXAR, 200, CACHE_NO_EXISTE);
  }
  if (!env2.API_ORIGEN) return responder2(null, 200, CACHE_API_CAIDA);
  let respuestaApi;
  try {
    const cabeceras = { accept: "application/json" };
    const ip = request.headers.get("CF-Connecting-IP");
    if (ip) cabeceras["X-Forwarded-For"] = ip;
    respuestaApi = await fetch(new URL(`/api/prestadores/${encodeURIComponent(slug)}`, env2.API_ORIGEN), {
      headers: cabeceras,
      signal: AbortSignal.timeout(ESPERA_MAXIMA_MS)
    });
  } catch {
    return responder2(null, 200, CACHE_API_CAIDA);
  }
  if (respuestaApi.status === 404) return responder2(META_NOINDEX, 404, CACHE_NO_EXISTE);
  if (!respuestaApi.ok) return responder2(null, 200, CACHE_API_CAIDA);
  let datos = null;
  try {
    datos = datosDeFicha(await respuestaApi.json());
  } catch {
    datos = null;
  }
  if (datos === null) return responder2(null, 200, CACHE_API_CAIDA);
  return responder2(metaDeFicha(datos, slug, origenDelSitio(env2)), 200, CACHE_FICHA);
}
__name(onRequest, "onRequest");

// _comun/sitemap.js
var RUTAS_ESTATICAS = ["/", "/buscar", "/comunas", "/oficios", "/profesionales", "/terminos", "/privacidad"];
var MAXIMO_URLS = 5e4;
var POR_PAGINA = 1e3;
var PAGINAS_POR_OMISION = 45;
var ESPERA_MAXIMA_MS2 = 8e3;
var ErrorDeApi = class extends Error {
  static {
    __name(this, "ErrorDeApi");
  }
};
async function listarFichas(env2, request) {
  if (!env2.API_ORIGEN) throw new ErrorDeApi("Falta API_ORIGEN");
  const maxPaginas = Number.parseInt(env2.SITEMAP_MAX_PAGINAS, 10) > 0 ? Number.parseInt(env2.SITEMAP_MAX_PAGINAS, 10) : PAGINAS_POR_OMISION;
  const fichas = [];
  let cursor = null;
  for (let pagina = 0; pagina < maxPaginas; pagina++) {
    const url = new URL("/api/prestadores/mapa", env2.API_ORIGEN);
    url.searchParams.set("limite", String(POR_PAGINA));
    if (cursor) url.searchParams.set("cursor", cursor);
    const cabeceras = { accept: "application/json" };
    const ip = request.headers.get("CF-Connecting-IP");
    if (ip) cabeceras["X-Forwarded-For"] = ip;
    let cuerpo;
    try {
      const r = await fetch(url, { headers: cabeceras, signal: AbortSignal.timeout(ESPERA_MAXIMA_MS2) });
      if (!r.ok) throw new ErrorDeApi(`La API respondio ${r.status}`);
      cuerpo = await r.json();
    } catch (e) {
      throw e instanceof ErrorDeApi ? e : new ErrorDeApi("La API no respondio");
    }
    if (!cuerpo || !Array.isArray(cuerpo.prestadores)) throw new ErrorDeApi("Respuesta sin forma de mapa");
    for (const p of cuerpo.prestadores) {
      if (p && typeof p.slug === "string" && p.slug !== "") fichas.push({ slug: p.slug, publicadoEn: p.publicadoEn ?? null });
    }
    cursor = typeof cuerpo.cursorSiguiente === "string" && cuerpo.cursorSiguiente !== "" ? cuerpo.cursorSiguiente : null;
    if (cursor === null) return fichas;
  }
  return fichas;
}
__name(listarFichas, "listarFichas");
function fechaCorta(valor) {
  if (typeof valor !== "string") return null;
  const t = Date.parse(valor);
  return Number.isNaN(t) ? null : new Date(t).toISOString().slice(0, 10);
}
__name(fechaCorta, "fechaCorta");
function entradas(origen, fichas) {
  return [
    ...RUTAS_ESTATICAS.map((ruta) => ({ loc: `${origen}${ruta}`, lastmod: null })),
    ...fichas.map((f) => ({ loc: `${origen}/prestadores/${encodeURIComponent(f.slug)}`, lastmod: fechaCorta(f.publicadoEn) }))
  ];
}
__name(entradas, "entradas");
function partir(lista, maximo = MAXIMO_URLS) {
  const trozos = [];
  for (let i = 0; i < lista.length; i += maximo) trozos.push(lista.slice(i, i + maximo));
  return trozos.length > 0 ? trozos : [[]];
}
__name(partir, "partir");
function xmlDeUrls(lista) {
  const filas = lista.map((e) => `  <url><loc>${escaparHtml(e.loc)}</loc>${e.lastmod ? `<lastmod>${e.lastmod}</lastmod>` : ""}</url>`);
  return `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${filas.join("\n")}
</urlset>
`;
}
__name(xmlDeUrls, "xmlDeUrls");
function xmlDeIndice(origen, cantidad) {
  const filas = Array.from({ length: cantidad }, (_, i) => `  <sitemap><loc>${escaparHtml(`${origen}/sitemaps/${i + 1}.xml`)}</loc></sitemap>`);
  return `<?xml version="1.0" encoding="UTF-8"?>
<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${filas.join("\n")}
</sitemapindex>
`;
}
__name(xmlDeIndice, "xmlDeIndice");
var CABECERAS_XML = {
  "Content-Type": "application/xml; charset=utf-8",
  "X-Content-Type-Options": CABECERAS_DE_SEGURIDAD["X-Content-Type-Options"],
  "Strict-Transport-Security": CABECERAS_DE_SEGURIDAD["Strict-Transport-Security"]
};
function respuestaXml(xml, request) {
  return new Response(request.method === "HEAD" ? null : xml, {
    status: 200,
    headers: { ...CABECERAS_XML, "Cache-Control": "public, max-age=0, s-maxage=3600" }
  });
}
__name(respuestaXml, "respuestaXml");
function respuestaNoDisponible() {
  return new Response("Sitemap no disponible por ahora.\n", {
    status: 503,
    headers: { ...CABECERAS_XML, "Content-Type": "text/plain; charset=utf-8", "Retry-After": "300", "Cache-Control": "no-store" }
  });
}
__name(respuestaNoDisponible, "respuestaNoDisponible");
async function servirSitemap({ request, env: env2 }, numero, maximo = MAXIMO_URLS) {
  if (request.method !== "GET" && request.method !== "HEAD") return new Response(null, { status: 405, headers: { Allow: "GET, HEAD" } });
  let fichas;
  try {
    fichas = await listarFichas(env2, request);
  } catch {
    return respuestaNoDisponible();
  }
  const origen = origenDelSitio(env2);
  const trozos = partir(entradas(origen, fichas), maximo);
  if (numero === 0) {
    return respuestaXml(trozos.length === 1 ? xmlDeUrls(trozos[0]) : xmlDeIndice(origen, trozos.length), request);
  }
  if (trozos.length === 1 || numero < 1 || numero > trozos.length) {
    return new Response("No existe.\n", { status: 404, headers: { ...CABECERAS_XML, "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "public, max-age=0, s-maxage=60" } });
  }
  return respuestaXml(xmlDeUrls(trozos[numero - 1]), request);
}
__name(servirSitemap, "servirSitemap");

// sitemaps/[n].js
var onRequest2 = /* @__PURE__ */ __name((contexto) => {
  const crudo = Array.isArray(contexto.params.n) ? contexto.params.n[0] : contexto.params.n;
  const numero = /^\d{1,4}(\.xml)?$/.test(crudo ?? "") ? Number.parseInt(crudo, 10) : -1;
  return servirSitemap(contexto, numero);
}, "onRequest");

// t/[segmento].js
var ESPERA_MAXIMA_MS3 = 4e3;
var RUTA_PROPIA = /^\/(?![/\\])/;
function responder(estado, extra = {}) {
  return new Response(null, {
    status: estado,
    headers: {
      ...CABECERAS_DE_SEGURIDAD,
      "Cache-Control": "no-store",
      // La ficha no necesita saber por que tarjeta se llego.
      "Referrer-Policy": "no-referrer",
      ...extra
    }
  });
}
__name(responder, "responder");
async function onRequest3({ request, env: env2, params }) {
  if (request.method !== "GET") return responder(405, { Allow: "GET" });
  if (!env2.API_ORIGEN) return responder(503, { "Retry-After": "60" });
  const entrada = new URL(request.url);
  const destino = new URL(`/t/${encodeURIComponent(params.segmento)}${entrada.search}`, env2.API_ORIGEN);
  const cabeceras = new Headers({ "X-Forwarded-Proto": "https", "X-Forwarded-Host": entrada.host });
  const ip = request.headers.get("CF-Connecting-IP");
  if (ip) cabeceras.set("X-Forwarded-For", ip);
  let respuesta;
  try {
    respuesta = await fetch(destino, {
      method: "GET",
      headers: cabeceras,
      redirect: "manual",
      signal: AbortSignal.timeout(ESPERA_MAXIMA_MS3)
    });
  } catch {
    return responder(503, { "Retry-After": "30" });
  }
  if (respuesta.status === 302) {
    const ubicacion = respuesta.headers.get("Location") ?? "";
    return RUTA_PROPIA.test(ubicacion) ? responder(302, { Location: ubicacion }) : responder(502);
  }
  if (respuesta.status === 429) {
    return responder(429, { "Retry-After": respuesta.headers.get("Retry-After") ?? "900" });
  }
  return responder(502);
}
__name(onRequest3, "onRequest");

// api/[[ruta]].js
async function onRequest4({ request, env: env2 }) {
  if (!env2.API_ORIGEN) {
    return new Response(
      JSON.stringify({ title: "API no configurada", detail: "Falta la variable API_ORIGEN." }),
      { status: 503, headers: { "content-type": "application/json" } }
    );
  }
  const entrada = new URL(request.url);
  const destino = new URL(entrada.pathname + entrada.search, env2.API_ORIGEN);
  const peticion = new Request(destino, request);
  const ip = request.headers.get("CF-Connecting-IP");
  if (ip) peticion.headers.set("X-Forwarded-For", ip);
  peticion.headers.set("X-Forwarded-Proto", "https");
  peticion.headers.set("X-Forwarded-Host", entrada.host);
  return fetch(peticion);
}
__name(onRequest4, "onRequest");

// sitemap.xml.js
var onRequest5 = /* @__PURE__ */ __name((contexto) => servirSitemap(contexto, 0), "onRequest");

// _middleware.js
var VARIABLE_INTERRUPTOR = "MANTENIMIENTO";
var VARIABLE_PASE = "MANTENIMIENTO_PASE";
var COOKIE_PASE = "zass_pase";
var HORAS_DE_PASE = 8;
var PARAMETRO_PASE = "pase";
var REINTENTAR_EN = 3600;
var TEXTOS = {
  titulo: "ZASS! \xB7 Volvemos muy pronto",
  encabezado: "Estamos terminando de construir ZASS!",
  cuerpo: "Le estamos dando los \xFAltimos detalles a la plataforma. Volvemos muy pronto, y cuando abramos vas a poder encontrar al profesional o al maestro que necesitas, cerca de ti.",
  prestadores: "\xBFOfreces un servicio y quieres estar entre los primeros en aparecer? Escr\xEDbenos y te avisamos el d\xEDa que abrimos.",
  correo: "somoszaschile@gmail.com",
  pie: "Conecta con un toque"
};
function enMantenimiento(env2) {
  const valor = (env2?.[VARIABLE_INTERRUPTOR] ?? "").toString().trim().toLowerCase();
  return !(valor === "0" || valor === "false" || valor === "no");
}
__name(enMantenimiento, "enMantenimiento");
function sonIguales(a, b) {
  if (typeof a !== "string" || typeof b !== "string") return false;
  if (a.length !== b.length) return false;
  let diferencia = 0;
  for (let i = 0; i < a.length; i += 1) diferencia |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diferencia === 0;
}
__name(sonIguales, "sonIguales");
function cookieDelPase(cabecera) {
  if (typeof cabecera !== "string") return null;
  for (const trozo of cabecera.split(";")) {
    const [nombre, ...resto] = trozo.trim().split("=");
    if (nombre === COOKIE_PASE) return resto.join("=");
  }
  return null;
}
__name(cookieDelPase, "cookieDelPase");
function paginaDeMantenimiento() {
  return `<!doctype html>
<html lang="es-CL">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title>${TEXTOS.titulo}</title>
<style>
  :root { --amarillo: #F6C81E; --tinta: #1A1A1A; --crema: #FDF8E7; }
  * { box-sizing: border-box; }
  body {
    margin: 0; min-height: 100vh; display: grid; place-items: center;
    padding: 24px; background: var(--crema); color: var(--tinta);
    font-family: Montserrat, "Segoe UI", system-ui, -apple-system, sans-serif;
    line-height: 1.6;
  }
  main { max-width: 34rem; text-align: center; }
  .marca {
    font-size: clamp(2.5rem, 12vw, 4rem); font-weight: 800; letter-spacing: -0.02em;
    margin: 0 0 8px;
  }
  .marca span { color: var(--amarillo); -webkit-text-stroke: 1px var(--tinta); }
  h1 { font-size: clamp(1.25rem, 5vw, 1.6rem); margin: 0 0 16px; }
  p { margin: 0 0 16px; }
  .destacado {
    background: #fff; border: 2px solid var(--tinta); border-radius: 14px;
    padding: 16px 20px; margin-top: 24px; text-align: left;
  }
  a { color: var(--tinta); font-weight: 700; }
  footer { margin-top: 28px; font-size: 0.9rem; opacity: 0.75; }
</style>
</head>
<body>
<main>
  <p class="marca">ZAS<span>S!</span></p>
  <h1>${TEXTOS.encabezado}</h1>
  <p>${TEXTOS.cuerpo}</p>
  <div class="destacado">
    <p>${TEXTOS.prestadores}</p>
    <p style="margin:0"><a href="mailto:${TEXTOS.correo}">${TEXTOS.correo}</a></p>
  </div>
  <footer>${TEXTOS.pie}</footer>
</main>
</body>
</html>`;
}
__name(paginaDeMantenimiento, "paginaDeMantenimiento");
function respuestaDeMantenimiento(peticion) {
  const cabeceras = {
    "content-type": "text/html; charset=utf-8",
    // Que ni el navegador ni Cloudflare se queden con esta pagina: el dia que se abra, se
    // abre para todos y no solo para quien limpie su cache.
    "cache-control": "no-store, must-revalidate",
    "retry-after": String(REINTENTAR_EN),
    "x-robots-tag": "noindex, nofollow"
  };
  const quiereHtml = (peticion.headers.get("accept") ?? "").includes("text/html");
  if (!quiereHtml) {
    return new Response(
      JSON.stringify({
        title: "En mantenimiento",
        status: 503,
        detail: "ZASS! est\xE1 en mantenimiento. Vuelve a intentarlo m\xE1s tarde."
      }),
      {
        status: 503,
        headers: {
          "content-type": "application/problem+json; charset=utf-8",
          "cache-control": "no-store, must-revalidate",
          "retry-after": String(REINTENTAR_EN),
          "x-robots-tag": "noindex, nofollow"
        }
      }
    );
  }
  return new Response(paginaDeMantenimiento(), { status: 503, headers: cabeceras });
}
__name(respuestaDeMantenimiento, "respuestaDeMantenimiento");
async function onRequest6(contexto) {
  const { request, env: env2, next } = contexto;
  if (!enMantenimiento(env2)) return next();
  const pase = (env2?.[VARIABLE_PASE] ?? "").toString();
  if (pase.length > 0) {
    const url = new URL(request.url);
    const pedido = url.searchParams.get(PARAMETRO_PASE);
    if (pedido !== null && sonIguales(pedido, pase)) {
      url.searchParams.delete(PARAMETRO_PASE);
      return new Response(null, {
        status: 302,
        headers: {
          location: url.pathname + (url.search || "") + url.hash,
          "set-cookie": `${COOKIE_PASE}=${pase}; Path=/; Max-Age=${HORAS_DE_PASE * 3600}; HttpOnly; Secure; SameSite=Lax`,
          "cache-control": "no-store"
        }
      });
    }
    if (sonIguales(cookieDelPase(request.headers.get("cookie")), pase)) return next();
  }
  return respuestaDeMantenimiento(request);
}
__name(onRequest6, "onRequest");

// ../.wrangler/tmp/pages-tsFaZK/functionsRoutes-0.2866692303143079.mjs
var routes = [
  {
    routePath: "/prestadores/:slug",
    mountPath: "/prestadores",
    method: "",
    middlewares: [],
    modules: [onRequest]
  },
  {
    routePath: "/sitemaps/:n",
    mountPath: "/sitemaps",
    method: "",
    middlewares: [],
    modules: [onRequest2]
  },
  {
    routePath: "/t/:segmento",
    mountPath: "/t",
    method: "",
    middlewares: [],
    modules: [onRequest3]
  },
  {
    routePath: "/api/:ruta*",
    mountPath: "/api",
    method: "",
    middlewares: [],
    modules: [onRequest4]
  },
  {
    routePath: "/sitemap.xml",
    mountPath: "/",
    method: "",
    middlewares: [],
    modules: [onRequest5]
  },
  {
    routePath: "/",
    mountPath: "/",
    method: "",
    middlewares: [onRequest6],
    modules: []
  }
];

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/path-to-regexp/dist.es2015/index.js
function lexer(str) {
  var tokens = [];
  var i = 0;
  while (i < str.length) {
    var char = str[i];
    if (char === "*" || char === "+" || char === "?") {
      tokens.push({ type: "MODIFIER", index: i, value: str[i++] });
      continue;
    }
    if (char === "\\") {
      tokens.push({ type: "ESCAPED_CHAR", index: i++, value: str[i++] });
      continue;
    }
    if (char === "{") {
      tokens.push({ type: "OPEN", index: i, value: str[i++] });
      continue;
    }
    if (char === "}") {
      tokens.push({ type: "CLOSE", index: i, value: str[i++] });
      continue;
    }
    if (char === ":") {
      var name = "";
      var j = i + 1;
      while (j < str.length) {
        var code = str.charCodeAt(j);
        if (
          // `0-9`
          code >= 48 && code <= 57 || // `A-Z`
          code >= 65 && code <= 90 || // `a-z`
          code >= 97 && code <= 122 || // `_`
          code === 95
        ) {
          name += str[j++];
          continue;
        }
        break;
      }
      if (!name)
        throw new TypeError("Missing parameter name at ".concat(i));
      tokens.push({ type: "NAME", index: i, value: name });
      i = j;
      continue;
    }
    if (char === "(") {
      var count3 = 1;
      var pattern = "";
      var j = i + 1;
      if (str[j] === "?") {
        throw new TypeError('Pattern cannot start with "?" at '.concat(j));
      }
      while (j < str.length) {
        if (str[j] === "\\") {
          pattern += str[j++] + str[j++];
          continue;
        }
        if (str[j] === ")") {
          count3--;
          if (count3 === 0) {
            j++;
            break;
          }
        } else if (str[j] === "(") {
          count3++;
          if (str[j + 1] !== "?") {
            throw new TypeError("Capturing groups are not allowed at ".concat(j));
          }
        }
        pattern += str[j++];
      }
      if (count3)
        throw new TypeError("Unbalanced pattern at ".concat(i));
      if (!pattern)
        throw new TypeError("Missing pattern at ".concat(i));
      tokens.push({ type: "PATTERN", index: i, value: pattern });
      i = j;
      continue;
    }
    tokens.push({ type: "CHAR", index: i, value: str[i++] });
  }
  tokens.push({ type: "END", index: i, value: "" });
  return tokens;
}
__name(lexer, "lexer");
function parse(str, options) {
  if (options === void 0) {
    options = {};
  }
  var tokens = lexer(str);
  var _a = options.prefixes, prefixes = _a === void 0 ? "./" : _a, _b = options.delimiter, delimiter = _b === void 0 ? "/#?" : _b;
  var result = [];
  var key = 0;
  var i = 0;
  var path = "";
  var tryConsume = /* @__PURE__ */ __name(function(type) {
    if (i < tokens.length && tokens[i].type === type)
      return tokens[i++].value;
  }, "tryConsume");
  var mustConsume = /* @__PURE__ */ __name(function(type) {
    var value2 = tryConsume(type);
    if (value2 !== void 0)
      return value2;
    var _a2 = tokens[i], nextType = _a2.type, index = _a2.index;
    throw new TypeError("Unexpected ".concat(nextType, " at ").concat(index, ", expected ").concat(type));
  }, "mustConsume");
  var consumeText = /* @__PURE__ */ __name(function() {
    var result2 = "";
    var value2;
    while (value2 = tryConsume("CHAR") || tryConsume("ESCAPED_CHAR")) {
      result2 += value2;
    }
    return result2;
  }, "consumeText");
  var isSafe = /* @__PURE__ */ __name(function(value2) {
    for (var _i = 0, delimiter_1 = delimiter; _i < delimiter_1.length; _i++) {
      var char2 = delimiter_1[_i];
      if (value2.indexOf(char2) > -1)
        return true;
    }
    return false;
  }, "isSafe");
  var safePattern = /* @__PURE__ */ __name(function(prefix2) {
    var prev = result[result.length - 1];
    var prevText = prefix2 || (prev && typeof prev === "string" ? prev : "");
    if (prev && !prevText) {
      throw new TypeError('Must have text between two parameters, missing text after "'.concat(prev.name, '"'));
    }
    if (!prevText || isSafe(prevText))
      return "[^".concat(escapeString(delimiter), "]+?");
    return "(?:(?!".concat(escapeString(prevText), ")[^").concat(escapeString(delimiter), "])+?");
  }, "safePattern");
  while (i < tokens.length) {
    var char = tryConsume("CHAR");
    var name = tryConsume("NAME");
    var pattern = tryConsume("PATTERN");
    if (name || pattern) {
      var prefix = char || "";
      if (prefixes.indexOf(prefix) === -1) {
        path += prefix;
        prefix = "";
      }
      if (path) {
        result.push(path);
        path = "";
      }
      result.push({
        name: name || key++,
        prefix,
        suffix: "",
        pattern: pattern || safePattern(prefix),
        modifier: tryConsume("MODIFIER") || ""
      });
      continue;
    }
    var value = char || tryConsume("ESCAPED_CHAR");
    if (value) {
      path += value;
      continue;
    }
    if (path) {
      result.push(path);
      path = "";
    }
    var open = tryConsume("OPEN");
    if (open) {
      var prefix = consumeText();
      var name_1 = tryConsume("NAME") || "";
      var pattern_1 = tryConsume("PATTERN") || "";
      var suffix = consumeText();
      mustConsume("CLOSE");
      result.push({
        name: name_1 || (pattern_1 ? key++ : ""),
        pattern: name_1 && !pattern_1 ? safePattern(prefix) : pattern_1,
        prefix,
        suffix,
        modifier: tryConsume("MODIFIER") || ""
      });
      continue;
    }
    mustConsume("END");
  }
  return result;
}
__name(parse, "parse");
function match(str, options) {
  var keys = [];
  var re = pathToRegexp(str, keys, options);
  return regexpToFunction(re, keys, options);
}
__name(match, "match");
function regexpToFunction(re, keys, options) {
  if (options === void 0) {
    options = {};
  }
  var _a = options.decode, decode = _a === void 0 ? function(x) {
    return x;
  } : _a;
  return function(pathname) {
    var m = re.exec(pathname);
    if (!m)
      return false;
    var path = m[0], index = m.index;
    var params = /* @__PURE__ */ Object.create(null);
    var _loop_1 = /* @__PURE__ */ __name(function(i2) {
      if (m[i2] === void 0)
        return "continue";
      var key = keys[i2 - 1];
      if (key.modifier === "*" || key.modifier === "+") {
        params[key.name] = m[i2].split(key.prefix + key.suffix).map(function(value) {
          return decode(value, key);
        });
      } else {
        params[key.name] = decode(m[i2], key);
      }
    }, "_loop_1");
    for (var i = 1; i < m.length; i++) {
      _loop_1(i);
    }
    return { path, index, params };
  };
}
__name(regexpToFunction, "regexpToFunction");
function escapeString(str) {
  return str.replace(/([.+*?=^!:${}()[\]|/\\])/g, "\\$1");
}
__name(escapeString, "escapeString");
function flags(options) {
  return options && options.sensitive ? "" : "i";
}
__name(flags, "flags");
function regexpToRegexp(path, keys) {
  if (!keys)
    return path;
  var groupsRegex = /\((?:\?<(.*?)>)?(?!\?)/g;
  var index = 0;
  var execResult = groupsRegex.exec(path.source);
  while (execResult) {
    keys.push({
      // Use parenthesized substring match if available, index otherwise
      name: execResult[1] || index++,
      prefix: "",
      suffix: "",
      modifier: "",
      pattern: ""
    });
    execResult = groupsRegex.exec(path.source);
  }
  return path;
}
__name(regexpToRegexp, "regexpToRegexp");
function arrayToRegexp(paths, keys, options) {
  var parts = paths.map(function(path) {
    return pathToRegexp(path, keys, options).source;
  });
  return new RegExp("(?:".concat(parts.join("|"), ")"), flags(options));
}
__name(arrayToRegexp, "arrayToRegexp");
function stringToRegexp(path, keys, options) {
  return tokensToRegexp(parse(path, options), keys, options);
}
__name(stringToRegexp, "stringToRegexp");
function tokensToRegexp(tokens, keys, options) {
  if (options === void 0) {
    options = {};
  }
  var _a = options.strict, strict = _a === void 0 ? false : _a, _b = options.start, start = _b === void 0 ? true : _b, _c = options.end, end = _c === void 0 ? true : _c, _d = options.encode, encode = _d === void 0 ? function(x) {
    return x;
  } : _d, _e = options.delimiter, delimiter = _e === void 0 ? "/#?" : _e, _f = options.endsWith, endsWith = _f === void 0 ? "" : _f;
  var endsWithRe = "[".concat(escapeString(endsWith), "]|$");
  var delimiterRe = "[".concat(escapeString(delimiter), "]");
  var route = start ? "^" : "";
  for (var _i = 0, tokens_1 = tokens; _i < tokens_1.length; _i++) {
    var token = tokens_1[_i];
    if (typeof token === "string") {
      route += escapeString(encode(token));
    } else {
      var prefix = escapeString(encode(token.prefix));
      var suffix = escapeString(encode(token.suffix));
      if (token.pattern) {
        if (keys)
          keys.push(token);
        if (prefix || suffix) {
          if (token.modifier === "+" || token.modifier === "*") {
            var mod = token.modifier === "*" ? "?" : "";
            route += "(?:".concat(prefix, "((?:").concat(token.pattern, ")(?:").concat(suffix).concat(prefix, "(?:").concat(token.pattern, "))*)").concat(suffix, ")").concat(mod);
          } else {
            route += "(?:".concat(prefix, "(").concat(token.pattern, ")").concat(suffix, ")").concat(token.modifier);
          }
        } else {
          if (token.modifier === "+" || token.modifier === "*") {
            throw new TypeError('Can not repeat "'.concat(token.name, '" without a prefix and suffix'));
          }
          route += "(".concat(token.pattern, ")").concat(token.modifier);
        }
      } else {
        route += "(?:".concat(prefix).concat(suffix, ")").concat(token.modifier);
      }
    }
  }
  if (end) {
    if (!strict)
      route += "".concat(delimiterRe, "?");
    route += !options.endsWith ? "$" : "(?=".concat(endsWithRe, ")");
  } else {
    var endToken = tokens[tokens.length - 1];
    var isEndDelimited = typeof endToken === "string" ? delimiterRe.indexOf(endToken[endToken.length - 1]) > -1 : endToken === void 0;
    if (!strict) {
      route += "(?:".concat(delimiterRe, "(?=").concat(endsWithRe, "))?");
    }
    if (!isEndDelimited) {
      route += "(?=".concat(delimiterRe, "|").concat(endsWithRe, ")");
    }
  }
  return new RegExp(route, flags(options));
}
__name(tokensToRegexp, "tokensToRegexp");
function pathToRegexp(path, keys, options) {
  if (path instanceof RegExp)
    return regexpToRegexp(path, keys);
  if (Array.isArray(path))
    return arrayToRegexp(path, keys, options);
  return stringToRegexp(path, keys, options);
}
__name(pathToRegexp, "pathToRegexp");

// ../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/wrangler/templates/pages-template-worker.ts
var escapeRegex = /[.+?^${}()|[\]\\]/g;
function getOriginalPathPrefix(requestPath, normalizedPrefix) {
  let end = 0;
  for (let index = 0; index < normalizedPrefix.length; index++) {
    end++;
    if (normalizedPrefix[index] === "/") {
      while (requestPath[end] === "/") {
        end++;
      }
    }
  }
  return requestPath.slice(0, end);
}
__name(getOriginalPathPrefix, "getOriginalPathPrefix");
function* executeRequest(request) {
  const requestPath = new URL(request.url).pathname;
  const normalizedRequestPath = requestPath.replace(/\/+/g, "/");
  for (const route of [...routes].reverse()) {
    if (route.method && route.method !== request.method) {
      continue;
    }
    const routeMatcher = match(route.routePath.replace(escapeRegex, "\\$&"), {
      end: false
    });
    const mountMatcher = match(route.mountPath.replace(escapeRegex, "\\$&"), {
      end: false
    });
    const matchResult = routeMatcher(requestPath) || routeMatcher(normalizedRequestPath);
    const originalMountMatch = mountMatcher(requestPath);
    const mountMatchResult = originalMountMatch || mountMatcher(normalizedRequestPath);
    if (matchResult && mountMatchResult) {
      const path = originalMountMatch ? originalMountMatch.path : getOriginalPathPrefix(requestPath, mountMatchResult.path);
      for (const handler of route.middlewares.flat()) {
        yield {
          handler,
          params: matchResult.params,
          path
        };
      }
    }
  }
  for (const route of routes) {
    if (route.method && route.method !== request.method) {
      continue;
    }
    const routeMatcher = match(route.routePath.replace(escapeRegex, "\\$&"), {
      end: true
    });
    const mountMatcher = match(route.mountPath.replace(escapeRegex, "\\$&"), {
      end: false
    });
    const matchResult = routeMatcher(requestPath);
    const mountMatchResult = mountMatcher(requestPath);
    if (matchResult && mountMatchResult && route.modules.length) {
      for (const handler of route.modules.flat()) {
        yield {
          handler,
          params: matchResult.params,
          path: matchResult.path
        };
      }
      break;
    }
  }
}
__name(executeRequest, "executeRequest");
var pages_template_worker_default = {
  async fetch(originalRequest, env2, workerContext) {
    let request = originalRequest;
    const handlerIterator = executeRequest(request);
    let data = {};
    let isFailOpen = false;
    const next = /* @__PURE__ */ __name(async (input, init) => {
      if (input !== void 0) {
        let url = input;
        if (typeof input === "string") {
          url = new URL(input, request.url).toString();
        }
        request = new Request(url, init);
      }
      const result = handlerIterator.next();
      if (result.done === false) {
        const { handler, params, path } = result.value;
        const context2 = {
          request: new Request(request.clone()),
          functionPath: path,
          next,
          params,
          get data() {
            return data;
          },
          set data(value) {
            if (typeof value !== "object" || value === null) {
              throw new Error("context.data must be an object");
            }
            data = value;
          },
          env: env2,
          waitUntil: workerContext.waitUntil.bind(workerContext),
          passThroughOnException: /* @__PURE__ */ __name(() => {
            isFailOpen = true;
          }, "passThroughOnException")
        };
        const response = await handler(context2);
        if (!(response instanceof Response)) {
          throw new Error("Your Pages function should return a Response");
        }
        return cloneResponse(response);
      } else if ("ASSETS") {
        const response = await env2["ASSETS"].fetch(request);
        return cloneResponse(response);
      } else {
        const response = await fetch(request);
        return cloneResponse(response);
      }
    }, "next");
    try {
      return await next();
    } catch (error3) {
      if (isFailOpen) {
        const response = await env2["ASSETS"].fetch(request);
        return cloneResponse(response);
      }
      throw error3;
    }
  }
};
var cloneResponse = /* @__PURE__ */ __name((response) => (
  // https://fetch.spec.whatwg.org/#null-body-status
  new Response(
    [101, 204, 205, 304].includes(response.status) ? null : response.body,
    response
  )
), "cloneResponse");
export {
  pages_template_worker_default as default
};
