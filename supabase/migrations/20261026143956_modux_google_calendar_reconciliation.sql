-- Reconciliar somente agenda/status, preservando contatos, serviços e histórico.
grant update(event_etag,sync_status) on public.agenda_calendar_links to authenticated;
create policy agenda_calendar_links_update
on public.agenda_calendar_links for update to authenticated
using (app_private.agenda_has_access(company_id))
with check (app_private.agenda_has_access(company_id));

create or replace function public.agenda_reconcile_google_event(
  p_company_id uuid,p_calendar_id text,p_event_id text,
  p_expected_etag text,p_observed_etag text,p_expected_revision integer,
  p_date date,p_time time,p_duration integer,p_cancelled boolean default false
) returns public.agenda_calendar_links
language plpgsql security invoker set search_path=''
as $$
declare
  linked public.agenda_calendar_links;
  appointment public.agenda_appointments;
begin
  if (select auth.uid()) is null then
    raise exception using errcode='42501',message='Autenticação necessária';
  end if;
  if p_cancelled is null or p_expected_etag is null or length(p_expected_etag)=0
    or p_observed_etag is null or length(p_observed_etag)=0 or length(p_observed_etag)>256
    or p_expected_revision is null or p_expected_revision<1
    or (not p_cancelled and (p_date is null or p_time is null or p_duration is null or p_duration<15 or p_duration>1440)) then
    raise exception using errcode='22023',message='Dados Google/revisão incompletos';
  end if;
  select * into linked from public.agenda_calendar_links l
  where l.company_id=p_company_id and l.calendar_id=p_calendar_id and l.event_id=p_event_id;
  if not found then
    raise exception using errcode='42501',message='Vínculo indisponível para esta conta';
  end if;
  -- Mesmo sentido de lock usado pela criação do vínculo: atendimento primeiro.
  select * into appointment from public.agenda_appointments a
  where a.company_id=p_company_id and a.id=linked.appointment_id and a.deleted_at is null for update;
  if not found then
    raise exception using errcode='42501',message='Atendimento indisponível';
  end if;
  select * into linked from public.agenda_calendar_links l
  where l.company_id=p_company_id and l.calendar_id=p_calendar_id and l.event_id=p_event_id for update;
  if not found then
    raise exception using errcode='42501',message='Vínculo indisponível';
  end if;
  -- Retentativa de resultado já confirmado não incrementa revisão nem gera auditoria repetida.
  if linked.event_etag=p_observed_etag and (
    (p_cancelled and linked.sync_status='excluido' and appointment.status='cancelado') or
    (not p_cancelled and linked.sync_status='sincronizado' and
      appointment.date is not distinct from p_date and appointment.time is not distinct from p_time
      and appointment.duration=p_duration)) then return linked; end if;
  if linked.event_etag is distinct from p_expected_etag or appointment.revision<>p_expected_revision then
    raise exception using errcode='40001',message='Vínculo/atendimento alterado; atualize antes de reconciliar';
  end if;
  if p_cancelled then
    update public.agenda_appointments set status='cancelado' where id=appointment.id and company_id=p_company_id;
  else
    update public.agenda_appointments set date=p_date,time=p_time,duration=p_duration
      where id=appointment.id and company_id=p_company_id;
  end if;
  update public.agenda_calendar_links set event_etag=p_observed_etag,
    sync_status=case when p_cancelled then 'excluido' else 'sincronizado' end
  where company_id=p_company_id and calendar_id=p_calendar_id and event_id=p_event_id
  returning * into linked;
  return linked;
end;
$$;
revoke all on function public.agenda_reconcile_google_event(uuid,text,text,text,text,integer,date,time,integer,boolean) from public,anon;
grant execute on function public.agenda_reconcile_google_event(uuid,text,text,text,text,integer,date,time,integer,boolean) to authenticated;
