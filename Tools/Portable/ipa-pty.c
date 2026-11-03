/* MIT: private PTY for a fixed ipatool command. No credentials in argv or files. */
#ifdef __APPLE__
#include <util.h>
#else
#include <pty.h>
#endif
#include <unistd.h>
#include <termios.h>
#include <poll.h>
#include <signal.h>
#include <sys/wait.h>
#include <errno.h>
#include <stdlib.h>
static volatile sig_atomic_t child;
static void stop(int sig) { if(child>0) { kill(-child,SIGKILL); int status; while(waitpid(child,&status,0)<0 && errno==EINTR) {} } _exit(128+sig); }
static int write_all(int fd,const char *p,ssize_t n){while(n>0){ssize_t w=write(fd,p,n);if(w<0){if(errno==EINTR)continue;return -1;}p+=w;n-=w;}return 0;}
int main(int argc,char **argv){
 if(argc!=2||argv[1][0]!='/')return 64;
 signal(SIGTERM,stop);signal(SIGINT,stop);signal(SIGHUP,stop);signal(SIGPIPE,SIG_IGN);
 int master;struct winsize size={.ws_row=60,.ws_col=1200};
 child=forkpty(&master,NULL,NULL,&size);
 if(child<0)return 70;
 if(child==0){struct termios term;if(tcgetattr(0,&term)!=0)_exit(71);term.c_lflag&=~(ECHO|ECHONL);if(tcsetattr(0,TCSANOW,&term)!=0)_exit(71);char *args[]={argv[1],"auth","login","--format","json",NULL};execv(argv[1],args);_exit(127);}
 struct pollfd fds[]={{0,POLLIN,0},{master,POLLIN,0}};char buffer[4096];
 for(;;){if(poll(fds,2,-1)<0){if(errno==EINTR)continue;stop(SIGTERM);}
  if(fds[1].revents&(POLLIN|POLLHUP|POLLERR)){ssize_t n=read(master,buffer,sizeof buffer);if(n<=0)break;if(write_all(1,buffer,n)!=0)stop(SIGTERM);}
  if(fds[0].revents&(POLLIN|POLLHUP|POLLERR)){ssize_t n=read(0,buffer,sizeof buffer);if(n<=0)stop(SIGTERM);if(write_all(master,buffer,n)!=0)break;}
 }
 close(master);int status;while(waitpid(child,&status,0)<0)if(errno!=EINTR)return 70;
 return WIFEXITED(status)?WEXITSTATUS(status):128+WTERMSIG(status);
}
