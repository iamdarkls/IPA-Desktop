// Exercises the same statically linked Unicorn JIT under per-process Windows CFG.
// No registry changes, credentials, Apple traffic or system-wide mitigation changes.
#define _WIN32_WINNT 0x0A00
#define NOMINMAX
#include <windows.h>
#include <unicorn/unicorn.h>
#include <unicorn/x86.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

static bool check(uc_err error) {
    if (error == UC_ERR_OK) return true;
    std::fprintf(stderr, "Unicorn failure: %s\n", uc_strerror(error));
    return false;
}
static void stop_hook(uc_engine* engine, uint64_t, uint32_t, void* value) {
    if (++*static_cast<int*>(value) == 20) uc_emu_stop(engine);
}
static int child() {
    PROCESS_MITIGATION_CONTROL_FLOW_GUARD_POLICY policy{};
    if (!GetProcessMitigationPolicy(GetCurrentProcess(), ProcessControlFlowGuardPolicy, &policy, sizeof(policy)) || !policy.EnableControlFlowGuard) {
        std::fprintf(stderr, "CFG was not enabled for the fixture process\n"); return 1;
    }
    uc_engine* engine = nullptr;
    if (!check(uc_open(UC_ARCH_X86, UC_MODE_64, &engine))) return 1;
    const uint8_t loop[] = {0x48,0xff,0xc0,0xeb,0xfb}; // inc rax; jmp back
    const uint8_t invalid[] = {0x0f,0x0b}; // ud2: exercise exception exit and recovery
    bool ok = check(uc_mem_map(engine,0x1000,0x2000,UC_PROT_ALL)) &&
        check(uc_mem_write(engine,0x1000,loop,sizeof(loop))) && check(uc_mem_write(engine,0x2000,invalid,sizeof(invalid)));
    for (int attempt=0; ok && attempt<20; ++attempt) {
        uint64_t value=0;
        ok = check(uc_reg_write(engine,UC_X86_REG_RAX,&value)) && check(uc_emu_start(engine,0x1000,0x1005,0,100)) &&
            check(uc_reg_read(engine,UC_X86_REG_RAX,&value)) && value==50;
        int calls=0; uc_hook hook{};
        if (ok) ok=check(uc_hook_add(engine,&hook,UC_HOOK_CODE,reinterpret_cast<void*>(stop_hook),&calls,1,0));
        if (ok) ok=check(uc_emu_start(engine,0x1000,0x1005,0,0)) && calls==20;
        if (hook) uc_hook_del(engine,hook);
        if (ok) ok=uc_emu_start(engine,0x2000,0x2002,0,0)==UC_ERR_INSN_INVALID;
    }
    uc_close(engine);
    if (!ok) { std::fprintf(stderr,"Unicorn exit/recovery invariant failed\n"); return 1; }
    return 0;
}
int main(int argc, char** argv) {
    if (argc==2 && std::strcmp(argv[1],"--cfg-child")==0) return child();
    wchar_t path[32768]; DWORD length=GetModuleFileNameW(nullptr,path,32768);
    if (!length || length>=32768) return 1;
    std::wstring command=L"\""+std::wstring(path,length)+L"\" --cfg-child";
    SIZE_T bytes=0; InitializeProcThreadAttributeList(nullptr,1,0,&bytes);
    if (!bytes) return 1;
    std::vector<uint8_t> storage(bytes);
    auto attributes=reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
    if (!InitializeProcThreadAttributeList(attributes,1,0,&bytes)) return 1;
    DWORD64 mitigation=PROCESS_CREATION_MITIGATION_POLICY_CONTROL_FLOW_GUARD_ALWAYS_ON;
    if (!UpdateProcThreadAttribute(attributes,0,PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY,&mitigation,sizeof(mitigation),nullptr,nullptr)) {
        DeleteProcThreadAttributeList(attributes); return 1;
    }
    STARTUPINFOEXW startup{}; startup.StartupInfo.cb=sizeof(startup); startup.lpAttributeList=attributes;
    PROCESS_INFORMATION process{};
    BOOL launched=CreateProcessW(path,command.data(),nullptr,nullptr,FALSE,EXTENDED_STARTUPINFO_PRESENT,nullptr,nullptr,&startup.StartupInfo,&process);
    DeleteProcThreadAttributeList(attributes);
    if (!launched) { std::fprintf(stderr,"CFG child launch failed: %lu\n",GetLastError()); return 1; }
    DWORD code=1;
    if (WaitForSingleObject(process.hProcess,30000)==WAIT_OBJECT_0) GetExitCodeProcess(process.hProcess,&code);
    else { TerminateProcess(process.hProcess,1); WaitForSingleObject(process.hProcess,5000); }
    CloseHandle(process.hThread); CloseHandle(process.hProcess);
    if (code) std::fprintf(stderr,"CFG child failed with exit code 0x%08lx\n",code);
    if (code==0) std::puts("PASS: CFG enabled; Unicorn instruction-limit, hook-stop and exception exits survived 20 offline cycles");
    return code==0 ? 0 : 1;
}
