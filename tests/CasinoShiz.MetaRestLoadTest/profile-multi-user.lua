local thread_number = 0
local request_number = 0
local thread_id = 0

setup = function(thread)
    thread:set("id", thread_number)
    thread_number = thread_number + 1
end

init = function()
    thread_id = wrk.thread:get("id")
end

request = function()
    request_number = request_number + 1
    wrk.headers["x-load-user"] = tostring(1000 + thread_id * 1000000 + request_number)
    return wrk.format("GET")
end
